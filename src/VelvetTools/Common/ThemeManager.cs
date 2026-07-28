using System.Windows;
using Microsoft.Win32;

namespace VelvetTools.Common;

/// <summary>
/// 主题管理：system（跟随系统）/ dark / light。
/// 调色板拆分在 Themes/Palette.Dark.xaml 与 Palette.Light.xaml，
/// 切换时替换 App 资源里的第一个合并字典；样式全部走 DynamicResource 即时生效。
/// </summary>
public static class ThemeManager
{
    public static event Action? Changed;

    private static string _mode = "system";
    private static bool _applied;

    public static string Mode => _mode;
    public static bool IsDarkEffective { get; private set; } = true;

    public static void Initialize(string mode)
    {
        _mode = NormalizeMode(mode);
        Apply(fireEvent: false);
    }

    public static void SetMode(string mode)
    {
        _mode = NormalizeMode(mode);
        Apply(fireEvent: true);
    }

    /// <summary>系统深浅色变化（WM_SETTINGCHANGE）时调用。</summary>
    public static void OnSystemThemeChanged()
    {
        if (_mode == "system")
            Apply(fireEvent: true);
    }

    private static string NormalizeMode(string mode)
        => mode is "dark" or "light" ? mode : "system";

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    private static void Apply(bool fireEvent)
    {
        bool dark = _mode == "dark" || (_mode == "system" && !SystemUsesLightTheme());
        if (_applied && dark == IsDarkEffective) return; // 无实际变化

        IsDarkEffective = dark;
        _applied = true;

        var app = Application.Current;
        if (app is null) return;

        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/Palette.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Add(dict);

        Logger.Info($"主题已切换：mode={_mode} dark={dark}");
        if (fireEvent) Changed?.Invoke();
    }
}
