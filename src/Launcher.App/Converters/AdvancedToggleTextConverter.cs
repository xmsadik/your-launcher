using System.Globalization;
using System.Windows.Data;

namespace YourLauncher.App.Converters;

/// <summary>Editor's collapsible "Advanced" section header text/arrow, per EditorViewModel.IsAdvancedExpanded.</summary>
public sealed class AdvancedToggleTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Advanced ▾" : "Advanced ▸";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
