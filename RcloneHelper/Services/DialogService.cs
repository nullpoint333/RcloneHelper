using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using RcloneHelper.Services.Abstractions;

namespace RcloneHelper.Services;

/// <summary>
/// 对话框服务实现
/// </summary>
public class DialogService : IDialogService
{
    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is Views.Windows.MainWindow mainWindow)
        {
            return mainWindow.ShowConfirmationAsync(message);
        }

        // 回退：同步返回 false
        return Task.FromResult(false);
    }

    public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string? fileTypeFilter = null, string? fileExtension = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = fileExtension ?? ".json",
            FileTypeChoices = fileTypeFilter != null
                ? new[] { new FilePickerFileType(fileTypeFilter) { Patterns = new[] { $"*{fileExtension ?? ".json"}" } } }
                : null
        };

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }

    public async Task<string?> ShowOpenFileDialogAsync(string title, string? fileTypeFilter = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypeFilter != null
                ? new[] { new FilePickerFileType(fileTypeFilter) { Patterns = new[] { "*.json" } } }
                : null
        };

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is TopLevel topLevel)
        {
            return topLevel;
        }
        return null;
    }
}
