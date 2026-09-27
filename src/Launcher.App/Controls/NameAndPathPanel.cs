using System.Windows;
using System.Windows.Controls;

namespace YourLauncher.App.Controls;

/// <summary>
/// Lays out a row's name (first child, left) and secondary text (second child, right-aligned) so the
/// <b>name wins</b>: it gets its full width as long as the secondary text keeps at least
/// <see cref="MinSecondaryFraction"/> of the row (or its own full width, if that's less). Whatever doesn't
/// fit is trimmed by the children themselves - end ellipsis on the name, start ellipsis on a
/// <see cref="PathText"/>.
/// </summary>
public sealed class NameAndPathPanel : Panel
{
    private const double MinSecondaryFraction = 0.4;
    private const double Gap = 16;

    private double _nameWidth;
    private double _secondaryWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count < 2)
        {
            return new Size(0, 0);
        }

        var name = InternalChildren[0];
        var secondary = InternalChildren[1];
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        name.Measure(unbounded);
        secondary.Measure(unbounded);

        var nameDesired = name.DesiredSize.Width;
        var secondaryDesired = secondary.DesiredSize.Width;
        var total = availableSize.Width;

        if (double.IsInfinity(total))
        {
            _nameWidth = nameDesired;
            _secondaryWidth = secondaryDesired;
        }
        else
        {
            var gap = secondaryDesired > 0 ? Gap : 0;
            var room = Math.Max(0, total - gap);
            if (nameDesired + secondaryDesired <= room)
            {
                _nameWidth = nameDesired;
                _secondaryWidth = secondaryDesired;
            }
            else
            {
                var secondaryFloor = Math.Min(secondaryDesired, room * MinSecondaryFraction);
                _secondaryWidth = Math.Min(secondaryDesired, Math.Max(room - nameDesired, secondaryFloor));
                _nameWidth = room - _secondaryWidth;
            }
        }

        name.Measure(new Size(_nameWidth, availableSize.Height));
        secondary.Measure(new Size(_secondaryWidth, availableSize.Height));

        var height = Math.Max(name.DesiredSize.Height, secondary.DesiredSize.Height);
        return new Size(double.IsInfinity(total) ? _nameWidth + _secondaryWidth : total, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count < 2)
        {
            return finalSize;
        }

        var name = InternalChildren[0];
        var secondary = InternalChildren[1];
        name.Arrange(new Rect(0, 0, _nameWidth, finalSize.Height));
        secondary.Arrange(new Rect(finalSize.Width - _secondaryWidth, 0, _secondaryWidth, finalSize.Height));
        return finalSize;
    }
}
