using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YourLauncher.App.Converters;

/// <summary>Visible when the bound string is non-null/non-empty (used for inline field errors and the target-not-found warning).</summary>
public sealed class StringNonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
