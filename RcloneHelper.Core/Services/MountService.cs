using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RcloneHelper.Helpers;
using RcloneHelper.Models;
using RcloneHelper.Services.Abstractions;

namespace RcloneHelper.Services;

/// <summary>
/// 挂载管理服务，负责管理所有挂载配置和相关操作
/// </summary>
public class MountService
{
    private readonly ISystemService _systemService;
    private readonly ILoggerService _logger;
    private readonly IConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly ConcurrentDictionary<string, Process> _mountProcesses = new();
    private readonly ConcurrentDictionary<string, byte> _cancelRequested = new();
    private readonly SynchronizationContext? _syncContext;
    private readonly SemaphoreSlim _configLock = new(1, 1);

    /// <summary>
    /// 挂载列表变化事件（新增或删除时触发）
    /// </summary>
    public event Action? MountsChanged;

    /// <summary>
    /// 挂载状态变化事件（挂载成功/卸载/意外退出时触发），用于刷新 UI 分组。
    /// </summary>
    public event Action? MountStatusChanged;

    public ObservableCollection<MountInfo> Mounts { get; } = new();

    /// <summary>
    /// 当前实际生效的 rclone 缓存目录
    /// </summary>
    public string EffectiveCacheDir => PathUtil.GetEffectiveCacheDir(_configService.Current.CacheDir);

    /// <summary>
    /// 触发挂载状态变化事件，并保证在创建服务时的线程（通常是 UI 线程）上执行。
    /// </summary>
    private void RaiseMountStatusChanged()
    {
        if (MountStatusChanged == null)
            return;

        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            _syncContext.Post(_ => MountStatusChanged?.Invoke(), null);
        else
            MountStatusChanged?.Invoke();
    }

    public MountService(
        ISystemService systemService,
        ILoggerService logger,
        IConfigService configService,
        INotificationService notificationService)
    {
        _systemService = systemService;
        _logger = logger;
        _configService = configService;
        _notificationService = notificationService;
        _syncContext = SynchronizationContext.Current; // 捕获 UI 线程的同步上下文
        LoadMounts();
    }

    #region 文件操作

    /// <summary>
    /// 从本地文件加载挂载配置
    /// </summary>
    public void LoadMounts()
    {
        Mounts.Clear();

        var configPath = PathUtil.MountsConfigPath;
        if (!File.Exists(configPath))
            return;

        try
        {
            var json = File.ReadAllText(configPath);
            var configs = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListMountConfig);
            if (configs == null)
                return;

            foreach (var config in configs)
            {
                var mount = MountInfo.FromConfig(config);
                Mounts.Add(mount);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载配置失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 保存挂载配置到本地文件
    /// </summary>
    public void SaveMounts()
    {
        try
        {
            var configs = Mounts.Select(m => m.ToConfig()).ToList();
            var json = JsonSerializer.Serialize(configs, AppJsonSerializerContext.Default.ListMountConfig);
            PathUtil.AtomicWriteAllText(PathUtil.MountsConfigPath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"保存配置失败: {ex.Message}", ex);
        }
    }

    #endregion 文件操作

    #region 挂载管理

    /// <summary>
    /// 添加新的挂载配置
    /// </summary>
    /// <param name="mount">挂载信息</param>
    public void AddMount(MountInfo mount)
    {
        if (mount == null)
            throw new ArgumentNullException(nameof(mount));

        if (string.IsNullOrWhiteSpace(mount.Name))
            throw new ArgumentException("挂载名称不能为空");

        // 统一走模型校验：S3 没有 URL，早期实现的 "URL 不能为空" 会直接挡住 S3 挂载
        var validationError = mount.Validate();
        if (validationError != null)
            throw new ArgumentException(validationError);

        // 检查是否已存在同名挂载
        if (Mounts.Any(m => m.Name == mount.Name))
            throw new InvalidOperationException($"已存在名为 {mount.Name} 的挂载");

        mount.IsMounted = false;
        mount.Status = "未挂载";
        Mounts.Add(mount);
        SaveMounts();
        MountsChanged?.Invoke();
    }

    /// <summary>
    /// 更新挂载配置
    /// </summary>
    /// <param name="mount">已更新的挂载对象（直接修改了属性）</param>
    /// <param name="originalName">原始名称（用于更新 rclone 配置，如果名称被修改）</param>
    public async Task UpdateMountAsync(MountInfo mount, string? originalName = null)
    {
        if (mount == null)
            throw new ArgumentNullException(nameof(mount));

        // 验证挂载对象存在于集合中
        if (!Mounts.Contains(mount))
            throw new InvalidOperationException($"未找到挂载对象");

        // 如果名称被修改，需要删除旧的 rclone 配置
        var oldName = originalName ?? mount.Name;
        if (!string.IsNullOrEmpty(oldName) && oldName != mount.Name)
        {
            await DeleteRcloneConfigAsync(oldName);
        }

        // 更新 rclone 配置（create 命令会创建或更新）
        await CreateRcloneConfigAsync(mount);

        SaveMounts();
    }

    /// <summary>
    /// 添加或更新挂载配置
    /// </summary>
    /// <param name="mount">挂载信息</param>
    public async Task AddOrUpdateMountAsync(MountInfo mount)
    {
        var existing = Mounts.FirstOrDefault(m => m.Name == mount.Name);
        if (existing != null)
        {
            existing.RemotePath = mount.RemotePath;
            existing.LocalDrive = mount.LocalDrive;
            existing.Type = mount.Type;
            existing.Url = mount.Url;
            existing.User = mount.User;
            existing.Password = mount.Password;
            existing.Vendor = mount.Vendor;
            existing.AutoMountOnStart = mount.AutoMountOnStart;
            await UpdateMountAsync(existing);
        }
        else
        {
            AddMount(mount);
        }
    }

    /// <summary>
    /// 删除挂载配置
    /// </summary>
    /// <param name="name">挂载名称</param>
    public void DeleteMount(string name)
    {
        var mount = Mounts.FirstOrDefault(m => m.Name == name);
        if (mount == null)
            return;

        // 如果已挂载，先卸载
        if (mount.IsMounted)
        {
            Unmount(name);
        }

        Mounts.Remove(mount);
        SaveMounts();
        MountsChanged?.Invoke();
    }

    /// <summary>
    /// 根据名称获取挂载信息
    /// </summary>
    /// <param name="name">挂载名称</param>
    /// <returns>挂载信息，如果不存在则返回 null</returns>
    public MountInfo? GetMount(string name)
    {
        return Mounts.FirstOrDefault(m => m.Name == name);
    }

    /// <summary>
    /// 获取可用的挂载点（跨平台）
    /// </summary>
    /// <returns>Windows: 盘符如 "Z:"; Linux/macOS: 目录路径如 "/mnt/rclone"</returns>
    public string GetAvailableMountPoint()
    {
        // 先检查已配置的挂载点
        var usedMountPoints = Mounts.Select(m => m.LocalDrive.ToUpper()).ToHashSet();
        var systemMountPoint = _systemService.GetAvailableMountPoint();

        // 如果系统建议的挂载点未被使用，直接返回
        if (!usedMountPoints.Contains(systemMountPoint.ToUpper()))
            return systemMountPoint;

        // 否则基于平台生成新的挂载点
        return GenerateAvailableMountPoint(usedMountPoints);
    }

    private string GenerateAvailableMountPoint(HashSet<string> usedMountPoints)
    {
        if (_systemService.Platform == OSPlatform.Windows)
        {
            // Windows: 生成盘符
            for (char c = 'Z'; c >= 'D'; c--)
            {
                var drive = $"{c}:";
                if (!usedMountPoints.Contains(drive))
                    return drive;
            }
            return "Z:";
        }
        else
        {
            // Linux/macOS: 生成目录路径
            var baseDir = _systemService.Platform == OSPlatform.OSX ? "/Volumes" : "/mnt";
            for (int i = 1; i <= 26; i++)
            {
                var mountPoint = $"{baseDir}/rclone{i}";
                if (!usedMountPoints.Contains(mountPoint.ToUpper()))
                    return mountPoint;
            }
            return $"{baseDir}/rclone";
        }
    }

    #endregion 挂载管理

    #region 挂载操作

    /// <summary>
    /// 执行挂载操作
    /// </summary>
    /// <param name="name">挂载名称</param>
    /// <param name="oldName">旧名称（如果名称变更需要删除旧的 rclone 配置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>挂载是否成功</returns>
    public async Task<bool> MountAsync(string name, string? oldName = null, CancellationToken cancellationToken = default)
    {
        var mount = GetMount(name);
        if (mount == null)
        {
            _logger.Error($"挂载失败: 未找到名为 {name} 的挂载");
            throw new InvalidOperationException($"未找到名为 {name} 的挂载");
        }

        if (mount.IsMounted)
            return true;

        // 设置挂载中状态
        mount.IsMounting = true;
        mount.Status = "正在挂载...";

        try
        {
            _logger.Info($"开始挂载: {name} ({mount.Type}) -> {mount.LocalDrive}");

            // 挂载前连通性预检：跑通才继续，跑不通直接终止并提示，不再启动 rclone 进程
            var (preflightOk, preflightMessage) = await TestConnectionAsync(mount, cancellationToken);
            if (!preflightOk)
            {
                mount.IsMounting = false;
                mount.Status = "挂载失败";
                mount.MountCancellationTokenSource = null;
                _logger.Error($"挂载前连通性测试失败: {name}, {preflightMessage}");
                throw new InvalidOperationException($"连通性测试失败，无法挂载：{preflightMessage}");
            }

            // 创建 rclone 配置（如果名称变更，删除旧的配置）
            await CreateRcloneConfigAsync(mount, oldName);

            // 清理可能的残留挂载状态（僵尸进程占用的 WinFsp 网络共享名等）
            await CleanupStaleMountAsync(mount);

            // 构建挂载参数
            var mountArgs = BuildMountArgs(mount);

            var mountProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = RcloneLocator.GetRclonePath(),
                    Arguments = mountArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            // 设置代理环境变量
            SetProxyEnvironment(mountProcess.StartInfo);

            mountProcess.Exited += (s, e) =>
                        {
                            // 在 UI 线程上执行状态更新和通知
                            void HandleExit()
                            {
                                _mountProcesses.TryRemove(name, out _);

                                // 取消上传时是我们主动结束进程的，不提示「意外断开」
                                var cancelledByUser = _cancelRequested.TryRemove(name, out _);

                                // 只有 IsMounted=true（曾经挂载成功过）才视为"意外断开"并通知用户。
                                // 启动阶段的退出由轮询循环统一报错，Exited 回调不重复处理。
                                if (mount.IsMounted && !cancelledByUser)
                                {
                                    mount.IsMounted = false;
                                    mount.Status = "未挂载";
                                    _logger.Warning($"挂载意外退出: {name}");
                                    _notificationService.ShowWarning($"挂载 \"{name}\" 已意外断开");
                                    RaiseMountStatusChanged();
                                }
                            }

                            // 如果有同步上下文，在 UI 线程执行；否则直接执行
                            if (_syncContext != null)
                                _syncContext.Post(_ => HandleExit(), null);
                            else
                                HandleExit();
                        };

            mountProcess.Start();
            _mountProcesses[name] = mountProcess;

            // 异步读取 stderr 到共享缓冲区。
            // 关键：rclone 在挂载失败时（例如 WinFsp 残留导致 80070050），
            //   会先把 "ERROR : ... Mount failed" 写到 stderr，然后**进程仍然存活**约 30 秒
            //   （rclone 在等待自己的 mount observer 超时）才退出。
            //   所以"轮询 HasExited"这条路会卡满 30 秒，必须主动监控 stderr 内容才能秒级响应。
            var stderrBuffer = new System.Text.StringBuilder();
            var stderrBufferLock = new object();
            var stderrReadTask = Task.Run(async () =>
            {
                try
                {
                    var reader = mountProcess.StandardError;
                    var chunk = new char[512];
                    while (!mountProcess.HasExited)
                    {
                        int read;
                        try
                        {
                            read = await reader.ReadAsync(chunk, 0, chunk.Length);
                        }
                        catch
                        {
                            break;
                        }
                        if (read <= 0) break;
                        lock (stderrBufferLock)
                        {
                            stderrBuffer.Append(chunk, 0, read);
                        }
                    }
                    // 进程退出后再读一次残留
                    try
                    {
                        var tail = await reader.ReadToEndAsync();
                        if (!string.IsNullOrEmpty(tail))
                        {
                            lock (stderrBufferLock)
                            {
                                stderrBuffer.Append(tail);
                            }
                        }
                    }
                    catch { /* 忽略 */ }
                }
                catch
                {
                    // stderr 读取失败不影响主流程
                }
            });

            // rclone 挂载失败的特征关键字（任一出现即视为启动失败）
            var fatalMarkers = new[]
            {
                "Mount failed",
                "Cannot create WinFsp-FUSE",
                "Fatal error",
                "failed to mount FUSE fs",
                "couldn't list files",
                "Statfs failed"
            };

            // 等待挂载完成：轮询检查挂载点是否可访问
            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(30);

            while (DateTime.UtcNow - startTime < timeout)
            {
                // 检查取消请求
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info($"挂载被取消: {name}");
                    // 终止进程
                    if (!mountProcess.HasExited)
                    {
                        mountProcess.Kill();
                        mountProcess.WaitForExit(5000);
                    }
                    _mountProcesses.TryRemove(name, out _);
                    mount.IsMounting = false;
                    mount.Status = "已取消";
                    mount.MountCancellationTokenSource = null;
                    throw new OperationCanceledException("挂载操作已取消", cancellationToken);
                }

                // 1) 进程已退出：读 stderr 并抛异常
                if (mountProcess.HasExited)
                {
                    // 等待 stderr 读取协程把残留读完
                    try { await stderrReadTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }

                    string stderrSnapshot;
                    lock (stderrBufferLock)
                    {
                        stderrSnapshot = stderrBuffer.ToString();
                    }

                    mount.IsMounting = false;
                    mount.Status = "挂载失败";
                    _logger.Error($"挂载失败: {name}, 进程已退出, 错误: {stderrSnapshot}");
                    throw new InvalidOperationException($"挂载失败: {stderrSnapshot}");
                }

                // 2) 进程还活着，但 stderr 已经写出失败关键字 → 立即终止并抛异常
                //    这是修复"卡 30 秒"的关键路径：不再等 rclone 自己的 observer 超时
                string currentStderr;
                lock (stderrBufferLock)
                {
                    currentStderr = stderrBuffer.ToString();
                }
                if (!string.IsNullOrEmpty(currentStderr))
                {
                    var matchedMarker = fatalMarkers.FirstOrDefault(marker =>
                        currentStderr.Contains(marker, StringComparison.OrdinalIgnoreCase));
                    if (matchedMarker != null)
                    {
                        _logger.Warning($"检测到挂载失败关键字 [{matchedMarker}]，立即终止 rclone 进程");

                        // 杀掉 rclone 进程
                        try
                        {
                            if (!mountProcess.HasExited)
                            {
                                mountProcess.Kill();
                                mountProcess.WaitForExit(3000);
                            }
                        }
                        catch (Exception killEx)
                        {
                            _logger.Warning($"终止 rclone 进程时出错: {killEx.Message}");
                        }

                        // 等待 stderr 协程完成（让它把剩余内容读完）
                        try { await stderrReadTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }

                        string finalStderr;
                        lock (stderrBufferLock)
                        {
                            finalStderr = stderrBuffer.ToString();
                        }

                        mount.IsMounting = false;
                        mount.Status = "挂载失败";
                        _logger.Error($"挂载失败: {name}, 错误: {finalStderr}");
                        throw new InvalidOperationException($"挂载失败: {finalStderr}");
                    }
                }

                // 3) 检查挂载点是否真的可用
                if (IsMountPointAccessible(mount.LocalDrive))
                {
                    // 进一步验证挂载点是否真正可用（能读取内容）
                    // 避免盘符存在但网络共享名损坏的情况
                    if (IsMountPointFunctional(mount.LocalDrive))
                    {
                        mount.IsMounting = false;
                        mount.IsMounted = true;
                        mount.Status = $"已挂载到 {mount.LocalDrive}";
                        mount.MountCancellationTokenSource = null;
                        _logger.Info($"挂载成功: {name} -> {mount.LocalDrive}");
                        RaiseMountStatusChanged();

                        return true;
                    }
                    else
                    {
                        // 盘符存在但无法读取内容，可能是 WinFsp 网络共享名损坏
                        // 继续等待或超时
                        _logger.Debug($"挂载点 {mount.LocalDrive} 存在但无法读取内容，继续等待...");
                    }
                }

                await Task.Delay(200, cancellationToken);
            }

            mount.IsMounting = false;
            mount.Status = "挂载超时";
            mount.MountCancellationTokenSource = null;
            _logger.Error($"挂载超时: {name}, 挂载点 {mount.LocalDrive} 未能访问");
            throw new InvalidOperationException($"挂载超时: 挂载点 {mount.LocalDrive} 在 {timeout.TotalSeconds} 秒内未能访问");
        }
        catch (OperationCanceledException)
        {
            // 取消操作已经在上面处理，直接抛出
            throw;
        }
        catch (Exception ex)
        {
            mount.IsMounting = false;
            mount.Status = "挂载失败";
            mount.MountCancellationTokenSource = null;
            _logger.Error($"挂载异常: {name}, 错误: {ex.Message}");
            throw new InvalidOperationException($"挂载错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 为 ProcessStartInfo 设置代理环境变量
    /// </summary>
    private void SetProxyEnvironment(ProcessStartInfo startInfo)
    {
        var config = _configService.Current;
        var proxyUrl = config.GetProxyUrl();

        if (string.IsNullOrWhiteSpace(proxyUrl))
            return;

        // 设置代理环境变量
        startInfo.Environment["HTTP_PROXY"] = proxyUrl;
        startInfo.Environment["HTTPS_PROXY"] = proxyUrl;
    }

    /// <summary>
    /// 构建 rclone mount 命令参数
    /// </summary>
    /// <remarks>
    /// 关于 --network-mode：rclone 在网络模式下会以 \\rclone\&lt;volname&gt; 注册一个 WinFsp 网络共享。
    /// 当同一会话内有多个并发挂载、或上次挂载未正常卸载时，新挂载会撞到残留共享名，
    /// 报 "Cannot create WinFsp-FUSE file system. The service rclone has failed to start (Status=80070050)"。
    /// 解决办法：
    ///   1. 默认走普通盘符模式（不注册网络共享名），从根本上避免冲突。
    ///   2. 即使在网络模式下，也使用进程 PID 拼到 volname 后缀，确保每个 rclone 进程的共享名唯一。
    /// </remarks>
    private string BuildMountArgs(MountInfo mount)
    {
        // remote path: name: 或 name:/path (rclone remote 名称需要转义特殊字符)
        // 对于包含特殊字符的名称，使用引号包裹
        var escapedName = EscapeRcloneName(mount.Name);
        var remotePath = BuildRemotePath(escapedName, mount);

        // 基础参数
        // --vfs-cache-mode writes: 写入立即刷新到远端
        // --no-modtime: 避免每次都改文件时间戳导致 WebDAV 服务器压力
        var args = new StringBuilder();
        args.Append("mount ").Append(remotePath).Append(' ').Append(QuoteArg(mount.LocalDrive));
        args.Append(" --vfs-cache-mode writes --links");

        // 缓存目录：用户可在「设置 → rclone 缓存」中自定义，修改后重新挂载生效。
        // 始终显式传递 --cache-dir，避免 rclone 默认落到 C 盘且行为不可控。
        var cacheDir = EffectiveCacheDir;
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch (Exception ex)
        {
            _logger.Warning($"创建 rclone 缓存目录失败: {cacheDir}, 错误: {ex.Message}");
        }
        args.Append(" --cache-dir ").Append(QuoteArg(cacheDir));

        // 缓存上限（可选）：留空则交给 rclone 默认行为（不限制）
        var cacheMaxSize = AppConfig.NormalizeCacheSize(_configService.Current.VfsCacheMaxSize);
        if (!string.IsNullOrWhiteSpace(cacheMaxSize))
            args.Append(" --vfs-cache-max-size ").Append(cacheMaxSize);

        // 网络模式
        if (mount.UseNetworkMode)
        {
            // 用 PID + 短随机后缀保证 volname 在同一会话内唯一，避免残留共享名冲突
            var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            args.Append(" --network-mode --volname ").Append(QuoteArg($@"\\rclone\{mount.Name}_{uniqueSuffix}"));
        }
        else
        {
            // 普通盘符模式：使用挂载名作为卷标，不走网络共享，从根本上规避 80070050 残留问题
            args.Append(" --volname ").Append(QuoteArg(mount.Name));
        }

        return args.ToString();
    }

    /// <summary>
    /// 构建 rclone 的远程路径部分（remote:path）。
    /// WebDAV 的路径已经包含在配置文件 url 里，FTP/SFTP 的配置只有 host/port，
    /// 路径必须在这里拼到 remote 后面，否则「路径」字段会被忽略。
    /// </summary>
    internal static string BuildRemotePath(string escapedName, MountInfo mount)
    {
        if (mount.IsS3Type)
        {
            var bucket = (mount.RemotePath ?? "").Trim().TrimStart('/');
            return string.IsNullOrWhiteSpace(bucket) ? $"{escapedName}:" : $"{escapedName}:{bucket}";
        }

        if (mount.IsFtpType || mount.IsSftpType)
        {
            var path = string.IsNullOrWhiteSpace(mount.Path) ? mount.RemotePath : mount.Path;
            path = (path ?? "").Trim().TrimStart('/');
            return string.IsNullOrWhiteSpace(path) ? $"{escapedName}:" : $"{escapedName}:/{path}";
        }

        return string.IsNullOrWhiteSpace(mount.RemotePath)
            ? $"{escapedName}:"
            : $"{escapedName}:{mount.RemotePath}";
    }

    /// <summary>
    /// 为命令行参数加引号（内部双引号转义），避免名称/路径中的特殊字符破坏命令
    /// </summary>
    private static string QuoteArg(string? value)
        => $"\"{(value ?? "").Replace("\"", "\\\"")}\"";

    /// <summary>
    /// 转义 rclone remote 名称中的特殊字符
    /// rclone 会自动处理中文和特殊字符，但如果名称包含空格或特殊字符，需要用引号包裹
    /// </summary>
    private static string EscapeRcloneName(string name)
    {
        // 如果名称包含空格或特殊字符，用引号包裹
        if (name.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == '\\'))
        {
            return $"\"{name}\"";
        }
        return name;
    }

    /// <summary>
    /// 取消正在进行的挂载操作
    /// </summary>
    /// <param name="name">挂载名称</param>
    public void CancelMount(string name)
    {
        var mount = GetMount(name);
        if (mount == null)
        {
            _logger.Warning($"取消挂载失败: 未找到名为 {name} 的挂载");
            return;
        }

        if (!mount.IsMounting)
        {
            _logger.Warning($"取消挂载失败: {name} 未在挂载中");
            return;
        }

        // 触发取消令牌
        mount.MountCancellationTokenSource?.Cancel();
        _logger.Info($"已请求取消挂载: {name}");
    }

    /// <summary>
    /// 执行卸载操作
    /// </summary>
    /// <param name="name">挂载名称</param>
    public void Unmount(string name)
    {
        var mount = GetMount(name);
        if (mount == null)
        {
            _logger.Error($"卸载失败: 未找到名为 {name} 的挂载");
            return;
        }

        if (!mount.IsMounted && !_mountProcesses.ContainsKey(name))
            return;

        if (string.IsNullOrWhiteSpace(mount.LocalDrive))
        {
            _logger.Error($"卸载失败: {name} 的挂载点为空，请刷新挂载状态后重试");
            return;
        }

        try
        {
            _logger.Info($"开始卸载: {name} ({mount.LocalDrive})");

            // 1. 终止挂载进程：优先从进程字典获取（窗口启动时已扫描填充）
            var processKilled = false;

            if (_mountProcesses.TryGetValue(name, out var trackedProcess) && !trackedProcess.HasExited)
            {
                trackedProcess.Kill();
                trackedProcess.WaitForExit(5000);
                processKilled = true;
            }

            // 2. Kill 后等待 WinFsp 释放驱动器号
            if (processKilled)
            {
                var waitStart = DateTime.UtcNow;
                while (DateTime.UtcNow - waitStart < TimeSpan.FromSeconds(3))
                {
                    if (!IsMountPointAccessible(mount.LocalDrive))
                        break;
                    Thread.Sleep(300);
                }
            }

            // 3. 执行 rclone umount（处理 Kill 后残留或无进程时的情况）
            if (IsMountPointAccessible(mount.LocalDrive))
            {
                var unmountProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = RcloneLocator.GetRclonePath(),
                        Arguments = $"umount {mount.LocalDrive}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                unmountProcess.Start();
                unmountProcess.WaitForExit(5000);

                var exitCode = unmountProcess.ExitCode;

                // 退出码: 0=成功, 2=非致命(挂载点已释放), 其他=错误
                if (exitCode != 0 && exitCode != 2)
                {
                    var error = unmountProcess.StandardError.ReadToEnd();
                    _logger.Error($"rclone umount 失败，退出码: {exitCode}, 错误: {error}");
                    throw new InvalidOperationException($"卸载失败: rclone umount 返回错误码 {exitCode}");
                }
                else if (exitCode == 2)
                {
                    var error = unmountProcess.StandardError.ReadToEnd();
                    _logger.Warning($"rclone umount 退出码 2（挂载点可能已释放）: {error}");
                }
            }

            // 4. 最终等待释放
            var maxWait = TimeSpan.FromSeconds(5);
            var finalWaitStart = DateTime.UtcNow;
            while (DateTime.UtcNow - finalWaitStart < maxWait)
            {
                if (!IsMountPointAccessible(mount.LocalDrive))
                    break;
                Thread.Sleep(300);
            }

            if (IsMountPointAccessible(mount.LocalDrive))
            {
                _logger.Error($"挂载点 {mount.LocalDrive} 在卸载后仍可访问");
                throw new InvalidOperationException($"卸载失败: 挂载点 {mount.LocalDrive} 仍然可访问");
            }

            mount.IsMounted = false;
            mount.Status = "未挂载";
            _mountProcesses.TryRemove(name, out _);
            _logger.Info($"卸载成功: {name}");
            RaiseMountStatusChanged();
        }
        catch (Exception ex)
        {
            _logger.Error($"卸载异常: {name}, 错误: {ex.Message}");
            throw new InvalidOperationException($"卸载错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 异步卸载操作
    /// </summary>
    /// <param name="name">挂载名称</param>
    public Task UnmountAsync(string name)
    {
        return Task.Run(() => Unmount(name));
    }

    /// <summary>
    /// 读取某个挂载的待上传缓存统计（文件数、字节数）。
    /// rclone 在 --vfs-cache-mode writes 下会先把写入落到 &lt;cache-dir&gt;/vfs/&lt;remote&gt;，
    /// 所以这个目录的大小约等于「已写入但还没上传完」的数据量。
    /// 纯文件 IO，调用方应放到后台线程执行。
    /// </summary>
    public (int Files, long Bytes) GetPendingUploadStats(MountInfo mount)
    {
        var dir = PathUtil.GetVfsCacheDir(EffectiveCacheDir, mount.Name);
        return PathUtil.GetDirectoryStats(dir);
    }

    /// <summary>
    /// 取消某个挂载的后台上传：
    /// 1) 立即结束 rclone 进程（避免它继续把缓存上传完）；
    /// 2) 删除该 remote 的 VFS 待上传缓存（仅杀进程还不够，rclone 下次挂载会续传）；
    /// 3) 按需重新挂载。
    /// </summary>
    /// <param name="name">挂载名称</param>
    /// <param name="remount">取消后是否自动重新挂载</param>
    /// <returns>释放的缓存字节数与重新挂载错误（成功为 null）</returns>
    public async Task<(long FreedBytes, string? RemountError)> CancelPendingUploadsAsync(string name, bool remount)
    {
        var mount = GetMount(name);
        if (mount == null)
            throw new InvalidOperationException($"未找到名为 {name} 的挂载");

        // 1. 立即结束进程：正常卸载会让 rclone 先完成队列中的上传，这里必须直接 Kill
        _cancelRequested[name] = 1;
        if (_mountProcesses.TryRemove(name, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"取消上传时结束 rclone 进程失败: {name}, 错误: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        // 2. 等待挂载点释放
        var waitStart = DateTime.UtcNow;
        while (IsMountPointAccessible(mount.LocalDrive)
               && DateTime.UtcNow - waitStart < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(200);
        }

        // 3. 丢弃待上传缓存
        var freed = await Task.Run(() => DiscardVfsCache(mount.Name));

        mount.IsMounted = false;
        mount.IsMounting = false;
        mount.Status = "未挂载";
        _cancelRequested.TryRemove(name, out _);
        _logger.Info($"已取消后台上传: {name}，丢弃缓存 {freed} 字节");
        RaiseMountStatusChanged();

        // 4. 重新挂载
        string? remountError = null;
        if (remount)
        {
            try
            {
                await MountAsync(name);
            }
            catch (Exception ex)
            {
                remountError = ex.Message;
                _logger.Error($"取消上传后重新挂载失败: {name}, 错误: {ex.Message}");
            }
        }

        return (freed, remountError);
    }

    /// <summary>
    /// 删除某个 remote 的 VFS 待上传缓存与元数据，返回释放的字节数
    /// </summary>
    private long DiscardVfsCache(string remoteName)
    {
        var cacheDir = EffectiveCacheDir;
        var freed = 0L;

        foreach (var dir in new[]
                 {
                     PathUtil.GetVfsCacheDir(cacheDir, remoteName),
                     PathUtil.GetVfsMetaDir(cacheDir, remoteName)
                 })
        {
            if (!Directory.Exists(dir))
                continue;

            freed += PathUtil.GetDirectorySize(dir);
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.Warning($"删除 VFS 缓存目录失败: {dir}, 错误: {ex.Message}");
            }
        }

        return freed;
    }

    #region 连接测试

    /// <summary>
    /// 测试挂载配置的连通性（保存前预检，不创建真实挂载）。
    /// WebDAV：HTTP(S) 请求目标 URL；FTP/SFTP：TCP 连接主机端口；
    /// S3：TCP 连接 Endpoint（未填写则仅校验必填项）。
    /// </summary>
    /// <param name="mount">待测试的挂载信息（通常为编辑中的副本）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否连通及提示消息</returns>
    public async Task<(bool Success, string Message)> TestConnectionAsync(MountInfo mount, CancellationToken cancellationToken = default)
    {
        if (mount == null)
            return (false, "测试失败：挂载信息为空");

        try
        {
            if (mount.IsWebDavType)
            {
                // 优先用完整 URL（含路径）做 HTTP 可达性测试，可同时校验账号密码
                var url = !string.IsNullOrWhiteSpace(mount.Host)
                    ? mount.ComputedUrl
                    : (mount.Url ?? "").Trim();
                if (string.IsNullOrWhiteSpace(url))
                    return (false, "请先填写主机地址");
                return await TestHttpUrlAsync(url, mount.User, mount.Password, cancellationToken);
            }

            if (mount.IsFtpType || mount.IsSftpType)
            {
                if (string.IsNullOrWhiteSpace(mount.Host))
                    return (false, "请先填写主机地址");
                return await TestTcpConnectionAsync(mount.Host.Trim(), mount.Port, mount.Type, cancellationToken);
            }

            if (mount.IsS3Type)
            {
                if (string.IsNullOrWhiteSpace(mount.AccessKeyId))
                    return (false, "请填写 Access Key ID");
                if (string.IsNullOrWhiteSpace(mount.SecretAccessKey))
                    return (false, "请填写 Secret Access Key");
                if (string.IsNullOrWhiteSpace(mount.RemotePath))
                    return (false, "请填写 Bucket 名称");

                var endpointHost = TryGetHost(mount.Endpoint);
                if (endpointHost == null)
                {
                    // 未填写 Endpoint（如 AWS 默认 endpoint）：仅校验必填项通过，
                    // 实际连通性需挂载时验证
                    return (true, "必填项完整，未填 Endpoint 时实际连通性需挂载验证");
                }

                var endpointPort = TryGetPort(mount.Endpoint, 443);
                return await TestTcpConnectionAsync(endpointHost, endpointPort, "s3-endpoint", cancellationToken);
            }

            // 其他类型：按 URL 做 HTTP 可达性测试
            if (string.IsNullOrWhiteSpace(mount.Url))
                return (false, "请先填写 URL");

            return await TestHttpUrlAsync(mount.Url.Trim(), mount.User, mount.Password, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warning($"连接测试异常: {ex.Message}");
            return (false, $"测试异常：{ex.Message}");
        }
    }

    private async Task<(bool Success, string Message)> TestTcpConnectionAsync(
        string host, int port, string label, CancellationToken cancellationToken)
    {
        if (port < 1 || port > 65535)
            return (false, "端口号范围应为 1-65535");

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await client.ConnectAsync(host, port, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return (false, $"连接超时：无法在 5 秒内连通 {host}:{port}");
            }

            return (true, $"连接成功：{label} {host}:{port} 可连通");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 外部取消挂载（如用户点了取消按钮），直接向上传递，不视为测试失败
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"连接失败：{host}:{port}（{ex.Message}）");
        }
    }

    private async Task<(bool Success, string Message)> TestHttpUrlAsync(
        string url, string user, string password, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler();
            var proxyUrl = _configService.Current.GetProxyUrl();
            if (!string.IsNullOrWhiteSpace(proxyUrl)
                && (proxyUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || proxyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                handler.Proxy = new WebProxy(proxyUrl);
                handler.UseProxy = true;
            }

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(user))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password ?? ""}"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
                return (true, $"服务器可连通，但需要检查用户名/密码（HTTP {status}）");
            if (status is >= 200 and < 500)
                return (true, $"连接成功（HTTP {status}）");
            return (true, $"服务器有响应（HTTP {status}），建议检查路径后挂载验证");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, $"连接超时：8 秒内 {url} 无响应");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"连接失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"连接失败：{ex.Message}");
        }
    }

    private static string? TryGetHost(string? urlOrHost)
    {
        if (string.IsNullOrWhiteSpace(urlOrHost))
            return null;
        var text = urlOrHost.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host;
        // 裸主机名（如 oss-cn-hangzhou.aliyuncs.com 或 host:port）
        var hostPart = text.Split('/')[0].Split(':')[0].Trim();
        return string.IsNullOrWhiteSpace(hostPart) ? null : hostPart;
    }

    private static int TryGetPort(string? urlOrHost, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(urlOrHost))
            return defaultPort;
        var text = urlOrHost.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !uri.IsDefaultPort)
            return uri.Port;
        var segments = text.Split('/')[0].Split(':');
        if (segments.Length >= 2 && int.TryParse(segments[^1], out var port) && port >= 1 && port <= 65535)
            return port;
        return defaultPort;
    }

    #endregion 连接测试

    /// <summary>
    /// 刷新所有挂载的状态
    /// </summary>
    public void RefreshMountStatus()
    {
        var activeMounts = _systemService.GetActiveMountNames();

        foreach (var mount in Mounts)
        {
            // 查找匹配的挂载点 (名称完全匹配或名称以 mount.Name 开头)
            var mountPoint = activeMounts.FirstOrDefault(m =>
                m.Key.Equals(mount.Name, StringComparison.OrdinalIgnoreCase) ||
                m.Key.StartsWith(mount.Name + ":", StringComparison.OrdinalIgnoreCase)).Value;

            if (!string.IsNullOrEmpty(mountPoint))
            {
                mount.IsMounted = true;
                mount.LocalDrive = mountPoint;
                mount.Status = $"已挂载到 {mount.LocalDrive}";
            }
            else
            {
                mount.IsMounted = false;
                mount.Status = "未挂载";
            }
        }

        RaiseMountStatusChanged();
    }

    /// <summary>
    /// 恢复进程字典：扫描系统中的 rclone 进程并与配置的挂载匹配
    /// （窗口启动后调用，直接扫描进程而不依赖保存的状态文件）
    /// </summary>
    public async Task RestoreMountProcessesAsync()
    {
        _logger.Info("开始扫描并恢复进程追踪");

        // 扫描系统中的 rclone 挂载进程（异步执行）
        var runningMounts = await Task.Run(() => _systemService.ScanRcloneMounts());
        if (runningMounts.Count == 0)
        {
            _logger.Info("未发现运行中的 rclone 挂载进程");
            return;
        }

        _logger.Info($"发现 {runningMounts.Count} 个运行中的 rclone 挂载进程");

        foreach (var (mountName, mountInfo) in runningMounts)
        {
            // 查找匹配的挂载配置
            var mountConfig = Mounts.FirstOrDefault(m =>
                m.Name.Equals(mountName, StringComparison.OrdinalIgnoreCase));

            if (mountConfig == null)
            {
                continue;
            }

            Process? proc = null;
            try
            {
                proc = Process.GetProcessById(mountInfo.Pid);
                if (proc.HasExited)
                {
                    continue;
                }

                if (_mountProcesses.TryAdd(mountConfig.Name, proc))
                {
                    // 更新挂载状态
                    mountConfig.IsMounted = true;
                    mountConfig.LocalDrive = mountInfo.DriveLetter;
                    mountConfig.Status = $"已挂载到 {mountInfo.DriveLetter}";
                    _logger.Info($"已恢复进程追踪: {mountConfig.Name} (PID: {mountInfo.Pid})");
                    proc = null; // 成功接管，不需要释放
                }
            }
            catch
            {
                // 进程不存在或无法访问，跳过
            }
            finally
            {
                proc?.Dispose();
            }
        }

        _logger.Info($"进程恢复完成，已追踪 {_mountProcesses.Count} 个进程");
    }

    #endregion 挂载操作

    #region 私有方法

    /// <summary>
    /// 检查挂载点是否可访问（用于判断挂载是否成功）
    /// </summary>
    /// <param name="mountPoint">挂载点路径（如 "Z:" 或 "/mnt/rclone"）</param>
    /// <returns>是否可访问</returns>
    private bool IsMountPointAccessible(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return false;

        try
        {
            // Windows: 检查盘符是否存在且可访问
            // Linux/macOS: 检查目录是否存在且可访问
            var info = new DirectoryInfo(mountPoint);
            return info.Exists;
        }
        catch
        {
            // 目录不存在或无法访问
            return false;
        }
    }

    /// <summary>
    /// 检查挂载点是否真正可用（能读取目录内容）
    /// 用于区分：挂载点存在但网络共享名损坏的情况
    /// </summary>
    private bool IsMountPointFunctional(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return false;

        try
        {
            var info = new DirectoryInfo(mountPoint);
            if (!info.Exists)
                return false;

            // 尝试获取目录内容，验证挂载点是否真正可用
            // GetFileSystemInfos() 会实际访问文件系统
            info.GetFileSystemInfos();
            return true;
        }
        catch
        {
            // 目录存在但无法读取内容，可能是网络共享名损坏
            return false;
        }
    }

    /// <summary>
    /// 清理挂载前的残留状态：僵尸进程占用的 WinFsp 网络共享名和残留挂载点
    /// </summary>
    /// <param name="mount">要挂载的配置信息</param>
    private async Task CleanupStaleMountAsync(MountInfo mount)
    {
        // 1. 检查挂载点是否已被占用（可能有其他挂载残留）
        if (!IsMountPointAccessible(mount.LocalDrive))
            return; // 挂载点未被占用，无需清理

        _logger.Info($"检测到挂载点 {mount.LocalDrive} 已被占用，尝试清理残留");

        // 2. 检查是否有对应的 rclone 进程在运行
        var activeRcloneMounts = _systemService.ScanRcloneMounts();

        // 查找是否有同名的活跃 rclone 进程
        var hasActiveProcess = activeRcloneMounts.TryGetValue(mount.Name, out var activeMountInfo);

        if (hasActiveProcess)
        {
            // 有活跃进程，尝试验证该进程是否真的正常工作
            try
            {
                var proc = Process.GetProcessById(activeMountInfo!.Pid);
                if (!proc.HasExited)
                {
                    // 进程仍然存活，检查挂载点是否可正常访问
                    if (IsMountPointAccessible(mount.LocalDrive))
                    {
                        // 进一步验证：尝试列出目录内容，确保挂载点真正可用
                        // 有些情况下挂载点存在但网络共享名损坏，导致无法读取内容
                        if (IsMountPointFunctional(mount.LocalDrive))
                        {
                            _logger.Info($"挂载点 {mount.LocalDrive} 已有活跃的 rclone 进程 (PID: {activeMountInfo.Pid})，接管该进程");

                        // 启用事件监听并注册退出事件
                        proc.EnableRaisingEvents = true;
                        proc.Exited += (s, e) =>
                        {
                            void HandleExit()
                            {
                                _mountProcesses.TryRemove(mount.Name, out _);
                                if (mount.IsMounted)
                                {
                                    mount.IsMounted = false;
                                    mount.Status = "未挂载";
                                    _logger.Warning($"挂载意外退出: {mount.Name}");
                                    _notificationService.ShowWarning($"挂载 \"{mount.Name}\" 已意外断开");
                                }
                            }

                            if (_syncContext != null)
                                _syncContext.Post(_ => HandleExit(), null);
                            else
                                HandleExit();
                        };

                        // 接管现有进程：加入追踪字典并更新状态
                        if (_mountProcesses.TryAdd(mount.Name, proc))
                        {
                            // 成功接管，更新挂载状态
                            mount.IsMounted = true;
                            mount.Status = $"已挂载到 {mount.LocalDrive}";
                            _logger.Info($"已接管现有挂载进程: {mount.Name} (PID: {activeMountInfo.Pid})");
                        }
                        else
                        {
                            // 已有同名进程在追踪中，释放当前进程句柄
                            proc.Dispose();
                        }
                        return;
                        }
                        else
                        {
                            // 挂载点存在但无法读取内容，可能是网络共享名损坏
                            _logger.Warning($"挂载点 {mount.LocalDrive} 存在但无法读取内容，可能是残留损坏，将清理后重新挂载");
                            proc.Dispose();
                        }
                        return;
                    }
                }
                proc.Dispose();
            }
            catch
            {
                // 进程已退出，继续清理
            }
        }

        // 3. 没有活跃进程但挂载点仍被占用 → 僵尸残留，尝试 rclone umount
        _logger.Info($"清理残留挂载点: {mount.LocalDrive}（无活跃 rclone 进程）");
        try
        {
            using var unmountProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = RcloneLocator.GetRclonePath(),
                    Arguments = $"umount {mount.LocalDrive}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            unmountProcess.Start();
            await unmountProcess.WaitForExitAsync();

            var exitCode = unmountProcess.ExitCode;
            if (exitCode == 0 || exitCode == 2)
            {
                _logger.Info($"残留挂载点清理完成: {mount.LocalDrive} (退出码: {exitCode})");
            }
            else
            {
                var error = await unmountProcess.StandardError.ReadToEndAsync();
                _logger.Warning($"清理残留挂载点返回非零退出码: {exitCode}, 错误: {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"清理残留挂载点失败: {mount.LocalDrive}, 错误: {ex.Message}");
        }

        // 4. 等待挂载点释放
        var waitStart = DateTime.UtcNow;
        while (DateTime.UtcNow - waitStart < TimeSpan.FromSeconds(5))
        {
            if (!IsMountPointAccessible(mount.LocalDrive))
            {
                _logger.Info($"残留挂载点 {mount.LocalDrive} 已释放");
                return;
            }
            await Task.Delay(300);
        }

        _logger.Warning($"残留挂载点 {mount.LocalDrive} 在清理后仍被占用");
    }

    private async Task CreateRcloneConfigAsync(MountInfo mount, string? oldName = null)
    {
        // 序列化对 rclone.conf 的写入，防止并发 config create 导致文件锁冲突
        await _configLock.WaitAsync();
        try
        {
            // 如果名称变更了，先删除旧的 remote 配置
            if (!string.IsNullOrEmpty(oldName) && oldName != mount.Name)
            {
                await DeleteRcloneConfigAsyncCore(oldName);
            }

            // 转义名称中的特殊字符
            var escapedName = EscapeRcloneName(mount.Name);

            // 不同后端需要的字段不同：
            //  - WebDAV: url/vendor/user/pass
            //  - FTP/SFTP: host/port/user/pass（早期版本只传 url，rclone 缺少 host 会直接失败）
            //  - S3: provider/access_key_id/secret_access_key/region/endpoint
            var configArgs = BuildConfigCreateArgs(escapedName, mount);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = RcloneLocator.GetRclonePath(),
                    Arguments = configArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            // 设置代理环境变量
            SetProxyEnvironment(process.StartInfo);

            _logger.Debug($"创建/更新 rclone 配置: {mount.Name}");
            process.Start();

            // 并发读取两个输出流，避免管道写满导致进程卡住
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await stdoutTask;

            if (process.ExitCode != 0)
            {
                var error = (await stderrTask).Trim();
                _logger.Error($"创建 rclone 配置失败: {mount.Name}, 错误: {error}");
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? $"写入 rclone 配置失败（退出码 {process.ExitCode}）"
                        : $"写入 rclone 配置失败：{error}");
            }
        }
        finally
        {
            _configLock.Release();
        }
    }

    /// <summary>
    /// 按后端类型生成 rclone config create 参数。
    /// rclone 的 config create 只接受目标后端真正拥有的配置项：
    /// FTP/SFTP 用 host/port，S3 用 provider/access_key_id 等，传 url 会被当成无效字段。
    /// </summary>
    internal static string BuildConfigCreateArgs(string escapedName, MountInfo mount)
    {
        var args = new StringBuilder();
        args.Append("config create ").Append(escapedName).Append(' ').Append(mount.Type).Append(' ');

        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            args.Append(key).Append(' ').Append(QuoteArg(value)).Append(' ');
        }

        if (mount.IsS3Type)
        {
            Add("provider", MapS3Provider(mount.S3Provider));
            Add("access_key_id", mount.AccessKeyId);
            Add("secret_access_key", mount.SecretAccessKey);
            Add("region", mount.Region);
            Add("endpoint", mount.Endpoint);
        }
        else if (mount.IsFtpType || mount.IsSftpType)
        {
            Add("host", mount.Host);
            Add("port", mount.Port > 0 ? mount.Port.ToString() : null);
            Add("user", mount.User);
            Add("pass", mount.Password);
        }
        else
        {
            Add("url", mount.Url);
            Add("user", mount.User);
            Add("pass", mount.Password);
            if (mount.Type == "webdav")
                Add("vendor", mount.Vendor);
        }

        // --obscure：密码字段一定按“未加密”处理，避免 rclone 把 22 位以上的
        // 纯 base64 明文误判成已加密内容而原样写入配置。
        args.Append("--obscure --non-interactive");
        return args.ToString();
    }

    /// <summary>
    /// 把界面上的 S3 服务商名称映射到 rclone 的 provider 值
    /// </summary>
    private static string MapS3Provider(string? provider) => (provider ?? "").Trim() switch
    {
        "" => "AWS",
        "Google" => "GCS",
        var other => other
    };

    private async Task DeleteRcloneConfigAsync(string name)
    {
        await _configLock.WaitAsync();
        try
        {
            await DeleteRcloneConfigAsyncCore(name);
        }
        finally
        {
            _configLock.Release();
        }
    }

    /// <summary>
    /// 删除 rclone 配置的内部实现（不含锁，由调用方负责加锁）
    /// </summary>
    private async Task DeleteRcloneConfigAsyncCore(string name)
    {
        try
        {
            var escapedName = EscapeRcloneName(name);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = RcloneLocator.GetRclonePath(),
                    Arguments = $"config delete {escapedName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();

            // 并发读取输出流，避免管道写满导致 WaitForExitAsync 卡住
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (Exception ex)
        {
            _logger.Error($"删除 rclone 配置失败: {name}, 错误: {ex.Message}");
        }
    }

    #endregion 私有方法
}