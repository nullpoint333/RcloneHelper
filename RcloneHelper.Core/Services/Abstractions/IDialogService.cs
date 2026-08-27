using System.Threading.Tasks;

namespace RcloneHelper.Services.Abstractions;

/// <summary>
/// 对话框服务接口
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 显示确认对话框
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <returns>用户是否点击了确认</returns>
    Task<bool> ShowConfirmationAsync(string title, string message);

    /// <summary>
    /// 显示文件保存对话框
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="defaultFileName">默认文件名</param>
    /// <param name="fileTypeFilter">文件类型过滤器（如 "JSON 文件 (*.json)"）</param>
    /// <param name="fileExtension">文件扩展名（如 ".json"）</param>
    /// <returns>用户选择的文件路径，取消则返回 null</returns>
    Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string? fileTypeFilter = null, string? fileExtension = null);

    /// <summary>
    /// 显示文件打开对话框
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="fileTypeFilter">文件类型过滤器（如 "JSON 文件 (*.json)"）</param>
    /// <returns>用户选择的文件路径，取消则返回 null</returns>
    Task<string?> ShowOpenFileDialogAsync(string title, string? fileTypeFilter = null);
}
