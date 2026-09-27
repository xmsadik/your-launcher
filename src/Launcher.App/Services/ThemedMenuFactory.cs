using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace YourLauncher.App.Services;

/// <summary>
/// Theme-aware <see cref="ContextMenu"/>/<see cref="MenuItem"/>/<see cref="Separator"/> styling, built
/// entirely in code rather than via App.xaml implicit styles (spec §5/§10 item 3: "TrayService static
/// styles → built per BuildMenu() from a shared theme-aware menu style factory (reused by §5)"). Originally
/// lived only in <see cref="TrayService"/> (Phase 5); extracted here so the panel's own right-click context
/// menu (Phase 6 Part B, spec §5) shares the exact same look instead of duplicating it.
///
/// A <see cref="ContextMenu"/> opened standalone (<c>IsOpen=true</c>, no PlacementTarget/owner in the
/// logical tree - the tray's case) doesn't reliably pick up Application-level implicit styles for its own
/// chrome in this WPF version; assigning the Style explicitly on each element sidesteps that resource-
/// lookup ambiguity entirely, and building fresh from the *current* theme resources on every open (rather
/// than once, statically) is what makes any such menu follow a theme change with no extra plumbing.
/// </summary>
public static class ThemedMenuFactory
{
    private static Brush ThemeBrush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Black;

    public static Style CreateMenuStyle()
    {
        var background = ThemeBrush("PanelBackgroundBrush");
        var foreground = ThemeBrush("ForegroundBrush");

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, background);
        border.SetValue(Border.BorderBrushProperty, ThemeBrush("PanelBorderBrush"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(2));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(TextElement.ForegroundProperty, foreground);

        var itemsHost = new FrameworkElementFactory(typeof(StackPanel));
        itemsHost.SetValue(Panel.IsItemsHostProperty, true);
        border.AppendChild(itemsHost);

        var template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
        var style = new Style(typeof(ContextMenu));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI Variable, Segoe UI")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.BackgroundProperty, background));
        style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(FrameworkElement.SnapsToDevicePixelsProperty, true));
        return style;
    }

    /// <summary>
    /// Row template: header content on the left, <see cref="MenuItem.InputGestureText"/> (the shortcut,
    /// e.g. "F2"/"Ctrl+X") right-aligned via a <see cref="DockPanel"/> - spec §5's "show the shortcut text
    /// in the menu's gesture column". A disabled item (spec: "Edit items disabled in read-only mode") dims
    /// instead of using the default WPF disabled look, which this fully custom template doesn't supply.
    /// </summary>
    public static Style CreateMenuItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

        var dock = new FrameworkElementFactory(typeof(DockPanel));

        var gestureText = new FrameworkElementFactory(typeof(TextBlock));
        gestureText.SetValue(DockPanel.DockProperty, Dock.Right);
        gestureText.SetValue(TextBlock.ForegroundProperty, ThemeBrush("SecondaryTextBrush"));
        gestureText.SetValue(FrameworkElement.MarginProperty, new Thickness(24, 0, 0, 0));
        gestureText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        gestureText.SetBinding(TextBlock.TextProperty, new Binding(nameof(MenuItem.InputGestureText)) { RelativeSource = RelativeSource.TemplatedParent });
        dock.AppendChild(gestureText);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        dock.AppendChild(content);

        border.AppendChild(dock);

        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };

        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Border.BackgroundProperty, ThemeBrush("HoverBackgroundBrush"), "Bd"));
        template.Triggers.Add(highlighted);

        var disabled = new Trigger { Property = MenuItem.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        template.Triggers.Add(disabled);

        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ThemeBrush("ForegroundBrush")));
        return style;
    }

    public static Style CreateSeparatorStyle()
    {
        var style = new Style(typeof(Separator));
        style.Setters.Add(new Setter(Control.BackgroundProperty, ThemeBrush("PanelBorderBrush")));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1.0));
        return style;
    }

    /// <summary>Builds a menu item styled with <paramref name="itemStyle"/> (from <see cref="CreateMenuItemStyle"/>) - shared by every menu that uses this factory.</summary>
    public static MenuItem CreateItem(string header, Style itemStyle, Action onClick, string gestureText = "", bool enabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = itemStyle,
            OverridesDefaultStyle = true,
            InputGestureText = gestureText,
            IsEnabled = enabled,
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    public static Separator CreateSeparator(Style separatorStyle) => new() { Style = separatorStyle };
}
