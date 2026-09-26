using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace YourLauncher.App.Controls;

/// <summary>
/// Attached-property pair (<see cref="TextProperty"/> + <see cref="PositionsProperty"/>) that renders a
/// TextBlock's Inlines as contiguous highlighted runs (accent color + SemiBold) over the given matched
/// char indices, with everything else left as plain runs. TextTrimming keeps working - it operates on
/// Inlines exactly as it would on plain Text.
/// </summary>
public static class HighlightedText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(HighlightedText), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty PositionsProperty = DependencyProperty.RegisterAttached(
        "Positions", typeof(IReadOnlyList<int>), typeof(HighlightedText), new PropertyMetadata(null, OnChanged));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);

    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    public static IReadOnlyList<int>? GetPositions(DependencyObject obj) => (IReadOnlyList<int>?)obj.GetValue(PositionsProperty);

    public static void SetPositions(DependencyObject obj, IReadOnlyList<int>? value) => obj.SetValue(PositionsProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Either property changing rebuilds from both current values, so it doesn't matter which order
        // WPF applies the two bindings in.
        if (d is TextBlock block)
        {
            Rebuild(block, GetText(block) ?? "", GetPositions(block));
        }
    }

    private static void Rebuild(TextBlock block, string text, IReadOnlyList<int>? positions)
    {
        block.Inlines.Clear();

        if (text.Length == 0)
        {
            return;
        }

        if (positions is null || positions.Count == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        var highlighted = new bool[text.Length];
        foreach (var p in positions)
        {
            if (p >= 0 && p < text.Length)
            {
                highlighted[p] = true;
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            var isHighlighted = highlighted[i];
            while (i < text.Length && highlighted[i] == isHighlighted)
            {
                i++;
            }

            var run = new Run(text[start..i]);
            if (isHighlighted)
            {
                // Theme accent brush swaps with the merged theme dictionary (spec §10 item 3) - a live
                // resource reference rather than a frozen static brush, so a Run created under one theme
                // keeps tracking the accent color if the theme changes while it's still on screen.
                run.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                run.FontWeight = FontWeights.SemiBold;
            }

            block.Inlines.Add(run);
        }
    }
}
