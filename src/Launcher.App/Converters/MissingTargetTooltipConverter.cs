using System.Globalization;
using System.Windows.Data;
using YourLauncher.App.Localization;

namespace YourLauncher.App.Converters;

/// <summary>Row tooltip for a missing app/path target (spec §7.3): a real string when missing, null (no tooltip shown) otherwise.</summary>
public sealed class MissingTargetTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Strings.ListMissingTarget : null;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
