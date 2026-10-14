using System.Globalization;
using System.Windows.Data;
using YourLauncher.App.Localization;

namespace YourLauncher.App.Converters;

/// <summary>Editor's Target row label: "URL" for a url node, "Target" for app/path.</summary>
public sealed class UrlOrTargetLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Strings.EditorUrlLabel : Strings.EditorTargetLabel;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
