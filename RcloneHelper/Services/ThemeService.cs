using System;
using Avalonia;
using Avalonia.Styling;

namespace RcloneHelper.Services;

/// <summary>
/// 主题服务：只负责切换 FluentTheme 的主题变体。
/// 颜色由 App.axaml 中的 ThemeDictionaries 提供，切换变体后
/// 所有 DynamicResource 会自动重新解析，无需在代码里维护两份颜色表。
/// </summary>
public static class ThemeService
{
    /// <summary>
    /// 应用深/浅色主题。
    /// </summary>
    /// <param name="isDark">true 为深色主题</param>
    public static void ApplyTheme(bool isDark)
    {
        if (Application.Current == null)
            return;

        var variant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (Application.Current.RequestedThemeVariant == variant)
            return;

        Application.Current.RequestedThemeVariant = variant;
    }
}