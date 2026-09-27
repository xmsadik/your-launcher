using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using YourLauncher.Core.Search;

namespace YourLauncher.App.Controls;

/// <summary>
/// Single-line breadcrumb text that, when too wide, is shortened from the <b>start</b> by
/// <see cref="PathTrimmer"/> ("… › Müşteri Sistem › Işıklı") - the end of a search result's path is
/// the part that tells results apart, which a TextBlock's end-ellipsis would cut off. Font and foreground
/// are the inherited TextElement values, like a TextBlock's. The full text is always in the tooltip.
/// </summary>
public sealed class PathText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PathText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextChanged));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(PathText), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private string _shown = "";

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var text = e.NewValue as string;
        ((PathText)d).ToolTip = string.IsNullOrEmpty(text) ? null : text;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? "";
        _shown = double.IsInfinity(availableSize.Width)
            ? text
            : PathTrimmer.TrimStart(text, availableSize.Width, s => Format(s).WidthIncludingTrailingWhitespace);

        var formatted = Format(_shown.Length > 0 ? _shown : " ");
        return new Size(_shown.Length > 0 ? formatted.WidthIncludingTrailingWhitespace : 0, formatted.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_shown.Length == 0)
        {
            return;
        }

        var formatted = Format(_shown);
        drawingContext.DrawText(formatted, new Point(Math.Max(0, RenderSize.Width - formatted.WidthIncludingTrailingWhitespace), 0));
    }

    private FormattedText Format(string text) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), TextElement.GetFontStyle(this), TextElement.GetFontWeight(this), TextElement.GetFontStretch(this)),
        TextElement.GetFontSize(this),
        Foreground,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
