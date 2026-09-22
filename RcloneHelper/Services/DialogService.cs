using System;
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

    public async Task<string?> ShowOpenFolderPickerAsync(string title, string? suggestedPath = null)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow?.StorageProvider is not { } storageProvider)
            {
                return null;
            }

            IStorageFolder? suggestedFolder = null;
            if (!string.IsNullOrWhiteSpace(suggestedPath))
            {
                try
                {
                    suggestedFolder = await storageProvider.TryGetFolderFromPathAsync(suggestedPath);
                }
                catch
                {
                    // 建议目录不可用时忽略，使用默认位置
                }
            }

            var options = new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedFolder
            };

            var folders = await storageProvider.OpenFolderPickerAsync(options);
            if (folders.Count == 0)
                return null;

            return folders[0].TryGetLocalPath();
        }
        catch
        {
            return null;
        }
    }
}