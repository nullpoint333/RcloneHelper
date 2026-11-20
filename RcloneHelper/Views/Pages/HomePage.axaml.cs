using System;
using Avalonia.Controls;
using Avalonia.Threading;
using RcloneHelper.Core.Pages;

namespace RcloneHelper.Views.Pages;

public partial class HomePage : UserControl
{
    private readonly DispatcherTimer _transferTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public HomePage()
    {
        InitializeComponent();

        // 定时刷新「后台上传中」的数据量（VFS 写缓存的待上传文件）
        _transferTimer.Tick += async (_, _) =>
        {
            if (DataContext is HomePageViewModel vm)
                await vm.RefreshPendingUploadsAsync();
        };

        // 每次页面显示时同步一次挂载状态（外部卸载/挂载也能及时反映）
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is HomePageViewModel vm)
                vm.Activate();

            _transferTimer.Start();
        };

        DetachedFromVisualTree += (_, _) => _transferTimer.Stop();
    }
}