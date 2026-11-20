using Avalonia.Controls;
using RcloneHelper.Core.Pages;

namespace RcloneHelper.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();

        // 每次页面显示时刷新缓存统计（惰性执行，避免拖慢启动）
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is SettingsPageViewModel vm)
                await vm.ActivateAsync();
        };
    }
}
