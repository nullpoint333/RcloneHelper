using CommunityToolkit.Mvvm.ComponentModel;

namespace RcloneHelper.Models;

/// <summary>
/// 依赖安装状态
/// </summary>
public enum DependencyStatus
{
    Unknown,
    Installed,
    NotInstalled
}

/// <summary>
/// 系统依赖项
/// </summary>
public partial class SystemDependency : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled))]
    [NotifyPropertyChangedFor(nameof(NeedsInstallation))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private DependencyStatus _status = DependencyStatus.Unknown;

    [ObservableProperty]
    private string _installUrl = "";

    [ObservableProperty]
    private string _icon = "";

    public bool IsInstalled => Status == DependencyStatus.Installed;
    public bool NeedsInstallation => Status == DependencyStatus.NotInstalled;

    /// <summary>
    /// 状态显示文本
    /// </summary>
    public string StatusText => Status switch
    {
        DependencyStatus.Installed => "已安装",
        DependencyStatus.NotInstalled => "未安装",
        _ => "未知"
    };
}
