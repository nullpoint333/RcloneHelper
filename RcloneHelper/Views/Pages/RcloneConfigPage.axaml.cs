using Avalonia.Controls;
using RcloneHelper.Core.Pages;

namespace RcloneHelper.Views.Pages;

public partial class RcloneConfigPage : UserControl
{
    public RcloneConfigPage()
    {
        InitializeComponent();

        // 每次页面显示时自动刷新依赖状态与 remote 列表
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is RcloneConfigPageViewModel vm)
                await vm.ActivateAsync();
        };
    }
}
