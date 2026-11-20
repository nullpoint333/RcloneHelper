using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneHelper.Helpers;
using RcloneHelper.Models;
using RcloneHelper.Services;
using RcloneHelper.Services.Abstractions;

namespace RcloneHelper.Core.Pages;

public partial class SettingsPageViewModel : ObservableObject
{
    private readonly ISystemService _systemService;
    private readonly INotificationService _notificationService;
    private readonly IConfigService _configService;
    private readonly MountService _mountService;
    private readonly IDialogService _dialogService;
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool _autoStartEnabled;

    [ObservableProperty]
    private bool _startSilently;

    [ObservableProperty]
    private bool _proxyEnabled;

    [ObservableProperty]
    private string _proxyProtocol = "http";

    [ObservableProperty]
    private string _proxyHost = "";

    [ObservableProperty]
    private int _proxyPort = 7890;

    [ObservableProperty]
    private string _proxyUser = "";

    [ObservableProperty]
    private string _proxyPassword = "";

    [ObservableProperty]
    private bool _isTestingProxy;

    /// <summary>
    /// 缓存目录草稿值（空表示使用默认位置）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCacheDir))]
    private string _cacheDir = "";

    /// <summary>
    /// 缓存上限草稿值（空表示不限制）
    /// </summary>
    [ObservableProperty]
    private string _vfsCacheMaxSize = "";

    [ObservableProperty]
    private string _cacheSizeText = "缓存占用：未统计";

    [ObservableProperty]
    private bool _isCacheBusy;

    /// <summary>
    /// 代理协议选项列表
    /// </summary>
    public string[] ProxyProtocolOptions { get; } = { "http", "https", "socks5" };

    /// <summary>
    /// 程序数据存放路径
    /// </summary>
    public string AppDataPath => PathUtil.AppDataDir;

    /// <summary>
    /// 程序所在位置
    /// </summary>
    public string AppLocation => PathUtil.AppLocation;

    /// <summary>
    /// 默认 rclone 缓存目录
    /// </summary>
    public string DefaultCacheDir => PathUtil.DefaultRcloneCacheDir;

    /// <summary>
    /// 当前实际生效的缓存目录
    /// </summary>
    public string EffectiveCacheDir => PathUtil.GetEffectiveCacheDir(CacheDir);

    /// <summary>
    /// 应用版本号
    /// </summary>
    public string AppVersionLabel
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            return $"Rclone Helper v{version}";
        }
    }

    public SettingsPageViewModel(
        ISystemService systemService,
        INotificationService notificationService,
        IConfigService configService,
        MountService mountService,
        IDialogService dialogService)
    {
        _systemService = systemService;
        _notificationService = notificationService;
        _configService = configService;
        _mountService = mountService;
        _dialogService = dialogService;

        CheckAutoStartStatus();
        LoadSettings();
        _isInitializing = false;
    }

    #region 系统设置

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_isInitializing) return;

        if (_systemService.SetAutoStart(value))
        {
            var message = value ? "已添加到开机启动" : "已移除开机启动";
            _notificationService.ShowSuccess(message);
        }
        else
        {
            _notificationService.ShowError("设置开机启动失败");
            AutoStartEnabled = !value;
        }
    }

    partial void OnStartSilentlyChanged(bool value)
    {
        if (_isInitializing) return;

        _configService.Update(c => c.StartSilently = value);
    }

    private void CheckAutoStartStatus()
    {
        AutoStartEnabled = _systemService.IsAutoStartEnabled;
    }

    private void LoadSettings()
    {
        var config = _configService.Current;
        StartSilently = config.StartSilently;
        ProxyEnabled = config.ProxyEnabled;
        ProxyProtocol = config.ProxyProtocol;
        ProxyHost = config.ProxyHost;
        ProxyPort = config.ProxyPort;
        ProxyUser = config.ProxyUser;
        ProxyPassword = SecureStorageHelper.Decrypt(config.ProxyPassword);
        CacheDir = config.CacheDir;
        VfsCacheMaxSize = config.VfsCacheMaxSize;
    }

    /// <summary>
    /// 页面被显示时调用：刷新缓存统计等惰性信息
    /// </summary>
    public async Task ActivateAsync()
    {
        try
        {
            CacheDir = _configService.Current.CacheDir;
            VfsCacheMaxSize = _configService.Current.VfsCacheMaxSize;
            await RefreshCacheSizeAsync();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"加载设置失败: {ex.Message}");
        }
    }

    #endregion

    #region 代理

    partial void OnProxyEnabledChanged(bool value)
    {
        if (_isInitializing) return;

        _configService.Update(c => c.ProxyEnabled = value);
    }

    partial void OnProxyProtocolChanged(string value)
    {
        if (_isInitializing) return;

        _configService.Update(c => c.ProxyProtocol = value);
    }

    partial void OnProxyHostChanged(string value)
    {
        if (_isInitializing) return;

        _configService.Update(c => c.ProxyHost = value);
    }

    partial void OnProxyPortChanged(int value)
    {
        if (_isInitializing) return;

        _configService.Update(c => c.ProxyPort = value);
    }

    /// <summary>
    /// 显式保存代理设置（页面本身改一项即自动保存，此按钮用于确认并提示）
    /// </summary>
    [RelayCommand]
    private void SaveProxy()
    {
        if (ProxyEnabled && string.IsNullOrWhiteSpace(ProxyHost))
        {
            _notificationService.ShowError("请填写代理服务器地址");
            return;
        }

        if (ProxyPort < 1 || ProxyPort > 65535)
        {
            _notificationService.ShowError("端口号范围应为 1-65535");
            return;
        }

        var enabled = ProxyEnabled;
        var protocol = ProxyProtocol;
        var host = ProxyHost;
        var port = ProxyPort;
        var user = ProxyUser;
        var password = SecureStorageHelper.Encrypt(ProxyPassword);
        _configService.Update(c =>
        {
            c.ProxyEnabled = enabled;
            c.ProxyProtocol = protocol;
            c.ProxyHost = host;
            c.ProxyPort = port;
            c.ProxyUser = user;
            c.ProxyPassword = password;
        });
        _notificationService.ShowSuccess("代理设置已保存，重新挂载后生效");
    }

    /// <summary>
    /// 测试代理服务器 TCP 连通性（5 秒超时）
    /// </summary>
    [RelayCommand]
    private async Task TestProxyConnectionAsync()
    {
        if (IsTestingProxy)
            return;

        if (!ProxyEnabled)
        {
            _notificationService.ShowWarning("请先启用代理");
            return;
        }

        var host = (ProxyHost ?? "").Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            _notificationService.ShowError("请填写代理服务器地址");
            return;
        }

        if (ProxyPort < 1 || ProxyPort > 65535)
        {
            _notificationService.ShowError("端口号范围应为 1-65535");
            return;
        }

        IsTestingProxy = true;
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, ProxyPort);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(connectTask, timeoutTask);
            if (completed == timeoutTask)
            {
                _notificationService.ShowError($"连接超时：无法在 5 秒内连通 {host}:{ProxyPort}");
                return;
            }

            await connectTask;
            _notificationService.ShowSuccess($"代理连接成功：{ProxyProtocol}://{host}:{ProxyPort}");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"代理连接失败：{ex.Message}");
        }
        finally
        {
            IsTestingProxy = false;
        }
    }

    #endregion

    #region rclone 缓存

    [RelayCommand]
    private async Task BrowseCacheDirAsync()
    {
        var picked = await _dialogService.ShowOpenFolderPickerAsync("选择 rclone 缓存目录", CacheDir);
        if (!string.IsNullOrWhiteSpace(picked))
            CacheDir = picked;
    }

    /// <summary>
    /// 保存缓存目录与缓存上限（两项一起写入配置）
    /// </summary>
    [RelayCommand]
    private void SaveCacheSettings()
    {
        if (!AppConfig.IsValidCacheSize(VfsCacheMaxSize))
        {
            _notificationService.ShowError("缓存上限格式不正确，示例：10G、500M，留空表示不限制");
            return;
        }

        var dir = "";
        if (!string.IsNullOrWhiteSpace(CacheDir))
        {
            dir = PathUtil.NormalizeDir(CacheDir);
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"无法创建缓存目录: {ex.Message}");
                return;
            }
        }

        var maxSize = AppConfig.NormalizeCacheSize(VfsCacheMaxSize);
        _configService.Update(c =>
        {
            c.CacheDir = dir;
            c.VfsCacheMaxSize = maxSize;
        });

        CacheDir = dir;
        VfsCacheMaxSize = maxSize;
        _notificationService.ShowSuccess("缓存设置已保存，重新挂载后生效");
        _ = RefreshCacheSizeAsync();
    }

    [RelayCommand]
    private async Task ResetCacheSettingsAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "恢复默认缓存设置",
            "将把缓存目录恢复为默认位置，并取消缓存上限。是否继续？");
        if (!confirmed)
            return;

        _configService.Update(c =>
        {
            c.CacheDir = "";
            c.VfsCacheMaxSize = "";
        });

        CacheDir = "";
        VfsCacheMaxSize = "";
        _notificationService.ShowSuccess("已恢复默认缓存设置，重新挂载后生效");
        await RefreshCacheSizeAsync();
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        var folder = EffectiveCacheDir;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"无法打开缓存目录: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理缓存目录内容（挂载运行中禁止清理，避免文件被占用）
    /// </summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var mounted = _mountService.Mounts.Count(m => m.IsMounted);
        if (mounted > 0)
        {
            _notificationService.ShowWarning($"还有 {mounted} 个连接处于挂载状态，请先全部卸载再清理缓存");
            return;
        }

        var folder = EffectiveCacheDir;
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "清理 rclone 缓存",
            $"将删除缓存目录中的所有内容：\n{folder}\n\n此操作不会影响远端文件，确定继续吗？");
        if (!confirmed)
            return;

        IsCacheBusy = true;
        CacheSizeText = "正在清理缓存…";
        try
        {
            var freed = await Task.Run(() => PathUtil.ClearDirectory(folder));
            _notificationService.ShowSuccess($"缓存已清理，释放 {PathUtil.FormatSize(freed)}");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"清理缓存失败: {ex.Message}");
        }
        finally
        {
            IsCacheBusy = false;
            await RefreshCacheSizeAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshCacheSizeAsync()
    {
        if (IsCacheBusy)
            return;

        IsCacheBusy = true;
        var folder = EffectiveCacheDir;
        CacheSizeText = "缓存占用：统计中…";
        try
        {
            var size = await Task.Run(() => PathUtil.GetDirectorySize(folder));
            CacheSizeText = $"缓存占用：{PathUtil.FormatSize(size)}";
        }
        catch
        {
            CacheSizeText = "缓存占用：无法统计";
        }
        finally
        {
            IsCacheBusy = false;
        }
    }

    #endregion

    #region 文件夹与配置

    [RelayCommand]
    private void OpenAppDataFolder()
    {
        OpenFolder(AppDataPath);
    }

    [RelayCommand]
    private void OpenAppLocationFolder()
    {
        OpenFolder(AppLocation);
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"无法打开文件夹: {ex.Message}");
        }
    }

    /// <summary>
    /// 导出所有挂载配置到 JSON 文件
    /// </summary>
    [RelayCommand]
    private void ExportConfig()
    {
        try
        {
            var exportPath = Path.Combine(AppDataPath, $"rclonehelper_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var configs = _mountService.Mounts.Select(m => m.ToConfig()).ToList();
            var json = JsonSerializer.Serialize(configs, AppJsonSerializerContext.Default.ListMountConfig);
            File.WriteAllText(exportPath, json);

            _notificationService.ShowSuccess($"配置已导出到: {exportPath}");

            // 打开导出目录
            Process.Start(new ProcessStartInfo
            {
                FileName = AppDataPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"导出失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 JSON 文件导入挂载配置
    /// </summary>
    [RelayCommand]
    private void ImportConfig()
    {
        try
        {
            // 查找最新的备份文件
            var backupFiles = Directory.GetFiles(AppDataPath, "rclonehelper_backup_*.json")
                .OrderByDescending(f => f)
                .ToList();

            if (backupFiles.Count == 0)
            {
                _notificationService.ShowWarning("未找到备份文件，请将备份文件放入配置目录后重试");
                OpenAppDataFolder();
                return;
            }

            // 使用最新的备份文件
            var importPath = backupFiles.First();
            var fileName = Path.GetFileName(importPath);

            var json = File.ReadAllText(importPath);
            var configs = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListMountConfig);

            if (configs == null || configs.Count == 0)
            {
                _notificationService.ShowWarning($"备份文件 {fileName} 中没有配置数据");
                return;
            }

            // 导入配置（跳过已存在的名称，单条失败不影响其他配置）
            var imported = 0;
            var skipped = 0;
            var failed = 0;
            foreach (var config in configs)
            {
                if (_mountService.Mounts.Any(m => m.Name == config.Name))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var mount = MountInfo.FromConfig(config);
                    _mountService.AddMount(mount);
                    imported++;
                }
                catch
                {
                    failed++;
                }
            }

            var summary = $"从 {fileName} 导入完成: 新增 {imported} 个，跳过 {skipped} 个已存在";
            if (failed > 0)
                summary += $"，{failed} 个配置无效";
            _notificationService.ShowSuccess(summary);
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"导入失败: {ex.Message}");
        }
    }

    #endregion
}