using System.Windows;
using System.Windows.Media;
using FluentIcons.Common;
using FluentIcons.Wpf;

namespace VelvetTools.Common;

/// <summary>
/// Creates UI glyphs from the MIT-licensed Microsoft Fluent UI System Icons
/// package. Keeping construction here prevents feature modules from falling
/// back to private-use Unicode values or operating-system icon fonts.
/// </summary>
internal static class AppIconFactory
{
    public static FluentIcon Create(
        Icon icon,
        double size = 16,
        Brush? foreground = null,
        IconVariant variant = IconVariant.Regular)
    {
        var control = new FluentIcon
        {
            Icon = icon,
            IconVariant = variant,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (foreground is not null)
            control.Foreground = foreground;
        return control;
    }
}
