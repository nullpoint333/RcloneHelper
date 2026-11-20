using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneHelper.Core.Models;
using RcloneHelper.Core.Pages;
using RcloneHelper.Services.Abstractions;

namespace RcloneHelper.Core.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISystemService _systemService;
    private readonly INotificationService _notificationService;
    private readonly IConfigService _configService;
    private readonly SynchronizationContext? _syncContext;
    private bool _hasInitialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private bool _isHomeSelected = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private bool _isRcloneSelected = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private bool _isSettingsSelected = false;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private bool _isDarkMode;

    public string AppVersion => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    /// <summary>
    /// 标题栏显示的当前页面名称
    /// </summary>
    public string PageTitle => IsHomeSelected ? "挂载管理" : IsRcloneSelected ? "Rclone 管理" : "设置";

    /// <summary>
    /// Toast 通知集合
    /// </summary>
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public HomePageViewModel HomePageViewModel { get; }
    public RcloneConfigPageViewModel RcloneConfigPageViewModel { get; }
    public SettingsPageViewModel SettingsPageViewModel { get; }

    public MainWindowViewModel(
        HomePageViewModel homePageViewModel,
        RcloneConfigPageViewModel rcloneConfigPageViewModel,
        SettingsPageViewModel settingsPageViewModel,
        ISystemService systemService,
        INotificationService notificationService,
        IConfigService configService)
    {
        HomePageViewModel = homePageViewModel;
        RcloneConfigPageViewModel = rcloneConfigPageViewModel;
        SettingsPageViewModel = settingsPageViewModel;
        _systemService = systemService;
        _notificationService = notificationService;
        _configService = configService;
        _syncContext = SynchronizationContext.Current;

        CurrentPage = HomePageViewModel;
        IsDarkMode = configService.Current.IsDarkMode;

        // 订阅通知事件
        _notificationService.NotificationRequested += OnNotificationRequested;
    }

    /// <summary>
    /// 处理通知请求。
    /// 通知可能来自后台线程（挂载结果、自动挂载等），必须切回 UI 线程再操作 Toast 集合；
    /// ToastItem 的入场/退场动画也会继承这个线程上下文。
    /// </summary>
    private void OnNotificationRequested(string message, NotificationType type, int duration)
    {
        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => OnNotificationRequested(message, type, duration), null);
            return;
        }

        var toast = new ToastItem(message, type.ToString().ToLower(), t =>
        {
            Toasts.Remove(t);
        });
        Toasts.Insert(0, toast);
        toast.StartTimer(duration);
    }

    [RelayCommand]
    private async Task WindowOpened()
    {
        // 只执行一次
        if (_hasInitialized) return;
        _hasInitialized = true;

        // 检查 FUSE 依赖
        var fuseDependency = _systemService.GetFuseDependency();
        if (fuseDependency?.NeedsInstallation == true)
        {
            // 强制导航到 Rclone 管理界面
            NavigateRclone();
            return;
        }

        // 刷新挂载状态
        HomePageViewModel.RefreshMountStatus();

        // 恢复进程追踪 + 自动挂载。
        // 全部在 UI 线程上顺序执行：挂载/卸载本身是异步的（等待而不是阻塞），
        // 这样 MountInfo 的属性变更始终发生在 UI 线程，避免后台线程改绑定属性。
        try
        {
            await HomePageViewModel.RestoreMountProcessesAsync();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"恢复挂载进程失败: {ex.Message}");
        }

        try
        {
            await HomePageViewModel.AutoMountConfiguredAsync();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"自动挂载失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        _configService.Update(c => c.IsDarkMode = IsDarkMode);
    }

    [RelayCommand]
    private void NavigateHome()
    {
        SelectPage(PageKind.Home);
        CurrentPage = HomePageViewModel;
    }

    [RelayCommand]
    private void NavigateRclone()
    {
        SelectPage(PageKind.Rclone);
        CurrentPage = RcloneConfigPageViewModel;
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        SelectPage(PageKind.Settings);
        CurrentPage = SettingsPageViewModel;
    }

    private enum PageKind
    {
        Home,
        Rclone,
        Settings
    }

    private void SelectPage(PageKind page)
    {
        IsHomeSelected = page == PageKind.Home;
        IsRcloneSelected = page == PageKind.Rclone;
        IsSettingsSelected = page == PageKind.Settings;
    }
}