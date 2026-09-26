using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YourLauncher.App.Converters;

/// <summary>
/// Row icon display priority (spec §4.3): IconImage → EmojiText → Glyph. Bound as a MultiBinding of
/// [IconImage, EmojiText]; <paramref name="parameter"/> ("Emoji" or "Glyph") picks which tier this
/// instance controls the visibility of. The IconImage element itself uses the simpler
/// <see cref="NullToVisibilityConverter"/> instead, since it only needs its own value.
/// </summary>
public sealed class IconTierVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasImage = values.Length > 0 && values[0] is not null;
        var hasEmoji = values.Length > 1 && !string.IsNullOrEmpty(values[1] as string);

        var visible = (parameter as string) switch
        {
            "Emoji" => !hasImage && hasEmoji,
            "Glyph" => !hasImage && !hasEmoji,
            _ => false,
        };

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
