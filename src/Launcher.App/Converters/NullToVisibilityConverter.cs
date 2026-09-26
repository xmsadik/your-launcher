using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YourLauncher.App.Converters;

/// <summary>Visible when the bound value is non-null (Phase 4: the row's IconImage - spec §4.3 display priority).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
