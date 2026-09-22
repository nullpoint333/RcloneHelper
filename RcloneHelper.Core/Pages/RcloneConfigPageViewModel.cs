using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using RcloneHelper.Helpers;
using RcloneHelper.Models;
using RcloneHelper.Services;
using RcloneHelper.Services.Abstractions;

namespace RcloneHelper.Core.Pages;

public partial class RcloneConfigPageViewModel : ObservableObject
{
    private readonly ISystemService _systemService;
    private readonly INotificationService _notificationService;
    private readonly IConfigService _configService;
    private readonly MountService _mountService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _rcloneVersion = "正在获取...";

    [ObservableProperty]
    private string _configFilePath = "正在获取...";

    [ObservableProperty]
    private string _cacheDir = "正在获取...";

    /// <summary>
    /// 默认缓存目录（用于“恢复默认”提示）
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
        IConfigService configService,
        MountService mountService,
        IDialogService dialogService)
    {
        _systemService = systemService;
        _notificationService = notificationService;
        _configService = configService;
        _mountService = mountService;
        _dialogService = dialogService;

        // 构造时只做零成本的赋值，重活（进程/磁盘探测、读配置文件）
        // 延迟到页面显示时的 RefreshConfigAsync，且放在后台线程执行
        ConfigFilePath = PathUtil.RcloneConfigPath;
        CacheDir = PathUtil.GetEffectiveCacheDir(_configService.Current.CacheDir);
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
        try
        {
            var folder = Path.GetDirectoryName(ConfigFilePath);
            if (folder != null && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        var folder = string.IsNullOrWhiteSpace(CacheDir)
            ? PathUtil.GetEffectiveCacheDir(_configService.Current.CacheDir)
            : CacheDir;
        if (!Directory.Exists(folder))
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"缓存目录不可用: {ex.Message}");
                return;
            }
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private async Task BrowseCacheDirAsync()
    {
        var picked = await _dialogService.ShowOpenFolderPickerAsync("选择 rclone 缓存目录", CacheDir);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            CacheDir = picked;
        }
    }

    [RelayCommand]
    private void SaveCacheDir()
    {
        var dir = (CacheDir ?? "").Trim();
        if (string.IsNullOrWhiteSpace(dir))
        {
            _notificationService.ShowError("缓存目录不能为空，为空请使用“恢复默认”");
            return;
        }

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"无法创建缓存目录: {ex.Message}");
            return;
        }

        _configService.Update(c => c.CacheDir = dir);
        CacheDir = dir;
        _notificationService.ShowSuccess("缓存目录已保存，重新挂载后生效");
    }

    [RelayCommand]
    private void ResetCacheDir()
    {
        _configService.Update(c => c.CacheDir = "");
        CacheDir = PathUtil.DefaultRcloneCacheDir;
        try
        {
            Directory.CreateDirectory(CacheDir);
        }
        catch { }
        _notificationService.ShowSuccess("已恢复默认缓存目录，重新挂载后生效");
    }

    [RelayCommand]
    private void CheckUpdate()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://rclone.org/downloads/",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private async Task RefreshConfig()
    {
        CacheDir = PathUtil.GetEffectiveCacheDir(_configService.Current.CacheDir);

        await UpdateDependenciesAsync();
        await RefreshRemotesAsync();
    }

    [RelayCommand]
    private async Task DeleteRemote()
    {
        if (SelectedRemote == null || string.IsNullOrWhiteSpace(SelectedRemote.Name))
            return;

        var remoteName = SelectedRemote.Name;

        try
        {
            // 执行外部进程放后台，避免 WaitForExit 卡住 UI
            await Task.Run(() =>
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = RcloneLocator.GetRclonePath(),
                        Arguments = $"config delete \"{remoteName}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);
            });
        }
        catch
        {
        }

        SelectedRemote = null;
        await RefreshRemotesAsync();
    }

    [RelayCommand]
    private async Task UnmountSelected()
    {
        if (SelectedRemote == null || !SelectedRemote.IsMounted)
            return;

        if (string.IsNullOrWhiteSpace(SelectedRemote.LocalDrive))
        {
            _notificationService.ShowError("卸载失败: 挂载点信息无效，请刷新后重试");
            return;
        }

        var remoteName = SelectedRemote.Name;
        var localDrive = SelectedRemote.LocalDrive;

        try
        {
            var rclonePath = RcloneLocator.GetRclonePath();
            if (string.IsNullOrEmpty(rclonePath))
            {
                _notificationService.ShowError("卸载失败: 未找到 rclone");
                return;
            }

            // 执行外部进程放后台，避免 WaitForExit 卡住 UI
            var result = await Task.Run(() =>
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = rclonePath,
                        Arguments = $"umount {localDrive}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var exited = process.WaitForExit(5000);

                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    return (Exited: false, ExitCode: -1);
                }

                return (Exited: true, ExitCode: process.ExitCode);
            });

            if (!result.Exited)
            {
                _notificationService.ShowError("卸载超时（5秒），请稍后重试或手动卸载");
                return;
            }

            if (result.ExitCode != 0)
            {
                _notificationService.ShowError($"卸载失败: rclone umount 返回 {result.ExitCode}");
                return;
            }

            _notificationService.ShowSuccess($"已卸载 {remoteName}");
            await RefreshRemotesAsync();
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
        var (fuse, rclone, version) = await Task.Run(() =>
        {
            var fuseDep = _systemService.GetFuseDependency();
            var rcloneDep = _systemService.GetRcloneDependency();

            var path = RcloneLocator.GetRclonePath();
            var ver = File.Exists(path) ? $"v{GetFileVersion(path)}" : "未找到 rclone";

            return (fuseDep, rcloneDep, ver);
        });

        FuseDependency = fuse;
        RcloneDependency = rclone;
        RcloneVersion = version;
    }

    [RelayCommand]
    private void OpenInstallUrl()
    {
        // 优先处理需要安装的依赖
        var dependency = FuseDependency?.NeedsInstallation == true ? FuseDependency :
                         RcloneDependency?.NeedsInstallation == true ? RcloneDependency :
                         FuseDependency?.IsInstalled == false ? FuseDependency :
                         RcloneDependency?.IsInstalled == false ? RcloneDependency : null;

        if (dependency == null || string.IsNullOrEmpty(dependency.InstallUrl))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dependency.InstallUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"无法打开链接: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshDependencies()
    {
        await UpdateDependenciesAsync();
        _notificationService.ShowSuccess("依赖状态已刷新");
    }

    #endregion
}

public partial class RemoteConfig : ObservableObject
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new();

    public string Type => Properties.TryGetValue("type", out var t) ? t : "未知";

    [ObservableProperty]
    private bool _isMounted;

    [ObservableProperty]
    private string _localDrive = "";
}