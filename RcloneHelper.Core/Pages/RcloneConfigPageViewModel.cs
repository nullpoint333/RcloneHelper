using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RcloneHelper.Helpers;
using RcloneHelper.Models;
using RcloneHelper.Services;
using RcloneHelper.Services.Abstractions;

namespace RcloneHelper.Core.Pages;

public partial class RcloneConfigPageViewModel : ObservableObject
{
    private readonly ISystemService _systemService;
    private readonly INotificationService _notificationService;
    private readonly MountService _mountService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _rcloneVersion = "正在获取...";

    [ObservableProperty]
    private string _configFilePath = "正在获取...";

    [ObservableProperty]
    private string _rclonePath = "";

    /// <summary>
    /// 实际生效的 rclone 缓存目录（只读展示，修改请到「设置」页）
    /// </summary>
    [ObservableProperty]
    private string _cacheDir = "";

    /// <summary>
    /// 默认缓存目录（用于提示）
    /// </summary>
    public string DefaultCacheDir => PathUtil.DefaultRcloneCacheDir;

    [ObservableProperty]
    private string _configContent = "";

    [ObservableProperty]
    private ObservableCollection<RemoteConfig> _remotes = new();

    [ObservableProperty]
    private RemoteConfig? _selectedRemote;

    [ObservableProperty]
    private SystemDependency? _fuseDependency;

    [ObservableProperty]
    private SystemDependency? _rcloneDependency;

    public RcloneConfigPageViewModel(
        ISystemService systemService,
        INotificationService notificationService,
        MountService mountService,
        IDialogService dialogService)
    {
        _systemService = systemService;
        _notificationService = notificationService;
        _mountService = mountService;
        _dialogService = dialogService;

        // 构造时只做零成本的赋值，重活（进程/磁盘探测、读配置文件）
        // 延迟到页面显示时的 ActivateAsync，且放在后台线程执行
        ConfigFilePath = PathUtil.RcloneConfigPath;
        CacheDir = _mountService.EffectiveCacheDir;
    }

    /// <summary>
    /// 页面显示时调用（依赖探测 + remote 列表都在后台线程执行）
    /// </summary>
    public async Task ActivateAsync()
    {
        try
        {
            CacheDir = _mountService.EffectiveCacheDir;
            await UpdateDependenciesAsync();
            await RefreshRemotesAsync();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"刷新 rclone 信息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析 rclone.conf（纯 I/O，可在后台线程调用）
    /// </summary>
    private static (string Content, List<RemoteConfig> Remotes) ParseRemotes(string configPath)
    {
        var remotes = new List<RemoteConfig>();
        try
        {
            if (!File.Exists(configPath))
                return ("配置文件不存在", remotes);

            var content = File.ReadAllText(configPath);
            RemoteConfig? currentRemote = null;

            foreach (var rawLine in content.Split('\n'))
            {
                var trimmed = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    continue;

                var remoteMatch = Regex.Match(trimmed, @"^\[([^\]]+)\]$");
                if (remoteMatch.Success)
                {
                    if (currentRemote != null)
                        remotes.Add(currentRemote);

                    currentRemote = new RemoteConfig { Name = remoteMatch.Groups[1].Value };
                    continue;
                }

                var kvMatch = Regex.Match(trimmed, @"^([^=]+)=(.*)$");
                if (kvMatch.Success && currentRemote != null)
                {
                    var key = kvMatch.Groups[1].Value.Trim();
                    var value = kvMatch.Groups[2].Value.Trim();
                    currentRemote.Properties[key] = value;
                }
            }

            if (currentRemote != null)
                remotes.Add(currentRemote);

            return (content, remotes);
        }
        catch (Exception ex)
        {
            return ($"无法读取配置: {ex.Message}", remotes);
        }
    }

    /// <summary>
    /// 刷新 remote 列表与挂载状态。
    /// 读配置 + 枚举挂载卷都在后台线程执行，避免卡住 UI。
    /// </summary>
    private async Task RefreshRemotesAsync()
    {
        var configPath = ConfigFilePath;

        var (content, remotes, activeMounts) = await Task.Run(() =>
        {
            var (text, list) = ParseRemotes(configPath);
            var active = _systemService.GetActiveMountNames();
            return (text, list, active);
        });

        ConfigContent = content;

        foreach (var remote in remotes)
        {
            // 匹配挂载点（名称完全匹配或名称以 remote.Name 开头）
            var mountPoint = activeMounts.FirstOrDefault(m =>
                m.Key.Equals(remote.Name, StringComparison.OrdinalIgnoreCase) ||
                m.Key.StartsWith(remote.Name + ":", StringComparison.OrdinalIgnoreCase)).Value;

            if (!string.IsNullOrEmpty(mountPoint))
            {
                remote.IsMounted = true;
                remote.LocalDrive = mountPoint;
            }
        }

        Remotes.Clear();
        foreach (var remote in remotes)
            Remotes.Add(remote);
    }

    private string GetFileVersion(string filePath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            return versionInfo.FileVersion ?? "未知";
        }
        catch
        {
            return "未知";
        }
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        var folder = Path.GetDirectoryName(ConfigFilePath);
        if (folder != null && Directory.Exists(folder))
            OpenFolder(folder);
    }

    /// <summary>
    /// 打开当前生效的缓存目录（缓存位置在「设置」页修改）
    /// </summary>
    [RelayCommand]
    private void OpenCacheFolder()
    {
        var folder = _mountService.EffectiveCacheDir;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"缓存目录不可用: {ex.Message}");
            return;
        }

        OpenFolder(folder);
    }

    [RelayCommand]
    private void CheckUpdate()
    {
        OpenUrl("https://rclone.org/downloads/");
    }

    [RelayCommand]
    private async Task RefreshConfig()
    {
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task DeleteRemote()
    {
        if (SelectedRemote == null || string.IsNullOrWhiteSpace(SelectedRemote.Name))
            return;

        var remoteName = SelectedRemote.Name;

        // 如果这个 remote 同时也是应用管理的挂载，走 MountService 删除，
        // 才能同时清掉 mounts.json，避免下次挂载又把配置写回来
        var managed = _mountService.GetMount(remoteName);
        var message = managed != null
            ? $"「{remoteName}」是当前管理的挂载，删除会同时移除应用中的连接配置。确定删除吗？"
            : $"确定从 rclone 配置中删除「{remoteName}」吗？此操作不可撤销。";

        if (!await _dialogService.ShowConfirmationAsync("删除 Remote", message))
            return;

        try
        {
            if (managed != null)
            {
                _mountService.DeleteMount(remoteName);
                _notificationService.ShowSuccess($"已删除 {remoteName}");
            }
            else
            {
                // 执行外部进程放后台，避免 WaitForExit 卡住 UI
                var exitCode = await Task.Run(() => RunRclone($"config delete {Quote(remoteName)}", 5000));
                if (exitCode == 0)
                {
                    _notificationService.ShowSuccess($"已从 rclone 配置中删除 {remoteName}");
                }
                else
                {
                    _notificationService.ShowError($"删除失败：rclone 返回 {exitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"删除失败: {ex.Message}");
            return;
        }

        SelectedRemote = null;
        await RefreshRemotesAsync();
    }

    [RelayCommand]
    private async Task UnmountSelected()
    {
        if (SelectedRemote == null || !SelectedRemote.IsMounted)
            return;

        var remoteName = SelectedRemote.Name;
        var localDrive = SelectedRemote.LocalDrive;

        try
        {
            // 优先让 MountService 处理（会同时终止进程并清理 WinFsp 残留）
            var managed = _mountService.GetMount(remoteName);
            if (managed is { IsMounted: true })
            {
                await _mountService.UnmountAsync(managed.Name);
                _notificationService.ShowSuccess($"已卸载 {remoteName}");
                await RefreshRemotesAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(localDrive))
            {
                _notificationService.ShowError("卸载失败: 挂载点信息无效，请刷新后重试");
                return;
            }

            // 外部挂载：退回 rclone umount
            var exitCode = await Task.Run(() => RunRclone($"umount {Quote(localDrive)}", 5000));
            if (exitCode == 0)
            {
                _notificationService.ShowSuccess($"已卸载 {remoteName}");
                await RefreshRemotesAsync();
            }
            else
            {
                _notificationService.ShowError($"卸载失败: rclone umount 返回 {exitCode}");
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"卸载异常: {ex.Message}");
        }
    }

    #region 设置和依赖

    /// <summary>
    /// 探测 rclone / WinFsp 依赖状态（进程与注册表调用放后台线程）
    /// </summary>
    private async Task UpdateDependenciesAsync()
    {
        var (fuse, rclone, version, path) = await Task.Run(() =>
        {
            var fuseDep = _systemService.GetFuseDependency();
            var rcloneDep = _systemService.GetRcloneDependency();

            var exePath = RcloneLocator.GetRclonePath();
            var ver = File.Exists(exePath) ? $"v{GetFileVersion(exePath)}" : "未找到 rclone";

            return (fuseDep, rcloneDep, ver, exePath);
        });

        FuseDependency = fuse;
        RcloneDependency = rclone;
        RcloneVersion = version;
        RclonePath = path;
    }

    /// <summary>
    /// 打开指定依赖的安装页面
    /// </summary>
    [RelayCommand]
    private void InstallDependency(SystemDependency? dependency)
    {
        if (dependency == null || string.IsNullOrWhiteSpace(dependency.InstallUrl))
            return;

        OpenUrl(dependency.InstallUrl);
    }

    [RelayCommand]
    private async Task RefreshDependencies()
    {
        await UpdateDependenciesAsync();
        _notificationService.ShowSuccess("依赖状态已刷新");
    }

    #endregion

    #region 辅助方法

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    /// <summary>
    /// 同步运行 rclone 命令（调用方负责放到后台线程），返回退出码；超时返回 -1。
    /// </summary>
    private static int RunRclone(string arguments, int timeoutMilliseconds)
    {
        var rclonePath = RcloneLocator.GetRclonePath();
        if (string.IsNullOrEmpty(rclonePath))
            return -1;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = rclonePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try { process.Kill(); } catch { /* ignore */ }
            return -1;
        }

        return process.ExitCode;
    }

    private void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"无法打开文件夹: {ex.Message}");
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"无法打开链接: {ex.Message}");
        }
    }

    #endregion
}

public partial class RemoteConfig : ObservableObject
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new();

    public string Type => Properties.TryGetValue("type", out var t) ? t : "未知";

    /// <summary>
    /// 类型显示名（用于徽标）
    /// </summary>
    public string TypeLabel => Type.ToLowerInvariant() switch
    {
        "webdav" => "WebDAV",
        "ftp" => "FTP",
        "sftp" => "SFTP",
        "s3" => "S3",
        var other => other.ToUpperInvariant()
    };

    [ObservableProperty]
    private bool _isMounted;

    [ObservableProperty]
    private string _localDrive = "";
}