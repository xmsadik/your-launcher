using YourLauncher.App.Localization;
using YourLauncher.App.Services;
using YourLauncher.Core.Calc;
using YourLauncher.Core.Providers;

namespace YourLauncher.App.Providers;

/// <summary>
/// Calculator row (tier <see cref="ResultTier.Calculator"/>, always first). All the "does this look like
/// math" logic is <see cref="CalcGate"/> in Core; this class only formats the row and wires Enter to the
/// clipboard. Id is constant ("calc:result") so usage history never accumulates per-expression noise.
/// </summary>
public sealed class CalculatorProvider : IResultProvider
{
    public IReadOnlyList<ProviderResult> Query(string query)
    {
        var expression = query.Trim();
        var result = CalcGate.TryEvaluate(expression);
        if (result is null)
        {
            return [];
        }

        var value = result.Value.Value;
        var text = CalcEvaluator.Format(value);
        var hex = CalcEvaluator.FormatHex(value);
        var subtitle = hex is null
            ? string.Format(Strings.CalcSubtitle, expression)
            : string.Format(Strings.CalcSubtitleHex, expression, hex);

        return
        [
            new ProviderResult
            {
                Id = "calc:result",
                Title = "= " + text,
                Subtitle = subtitle,
                Glyph = "", // Calculator
                Tier = ResultTier.Calculator,
                Score = 1000,
                // Follows settings.closeAfterLaunch like any other row: "type, Enter, paste elsewhere".
                CloseAfterRun = true,
                Execute = () => ClipboardHelper.TrySetText(text),
            },
        ];
    }
}
