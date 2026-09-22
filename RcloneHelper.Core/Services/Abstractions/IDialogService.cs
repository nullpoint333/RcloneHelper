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
    /// 显示文件夹选择对话框
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="suggestedPath">建议的初始目录（可为空）</param>
    /// <returns>用户选择的目录，未选择返回 null</returns>
    Task<string?> ShowOpenFolderPickerAsync(string title, string? suggestedPath = null);
}
