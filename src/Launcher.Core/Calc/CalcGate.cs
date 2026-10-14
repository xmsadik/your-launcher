namespace YourLauncher.Core.Calc;

/// <summary>
/// Decides whether a search-box query should produce a calculator row at all. Every keystroke goes through
/// here, so the rule is strict to keep ordinary searches clean: the query must parse COMPLETELY and must
/// contain at least one binary operator or function call plus at least one number/constant. That rejects
/// "notepad", "2048 game", "7-zip", "192.168.1.1", "pi", "0x1F" (a bare literal) and "-5" (unary minus only),
/// while "2+3", "3,5*2", "sqrt(16)" and "pi*2" pass. Residual false positives are inputs that really are
/// arithmetic, such as "555-1234" or "2024-10-14".
/// </summary>
public static class CalcGate
{
    public static CalcResult? TryEvaluate(string? query)
    {
        var result = CalcEvaluator.Evaluate(query);
        if (result is null)
        {
            return null;
        }

        var r = result.Value;
        return r.BinaryOperators + r.FunctionCalls >= 1 && r.Operands >= 1 ? result : null;
    }
}
