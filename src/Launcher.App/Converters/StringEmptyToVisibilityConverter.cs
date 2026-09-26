using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YourLauncher.App.Converters;

/// <summary>Visible when the bound string is null/empty (used for the search box's "Search…" placeholder).</summary>
public sealed class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
