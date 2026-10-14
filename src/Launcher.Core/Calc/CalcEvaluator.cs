using System.Globalization;

namespace YourLauncher.Core.Calc;

/// <summary>What a successfully evaluated expression looked like, beyond its value.</summary>
/// <param name="Value">Finite result.</param>
/// <param name="BinaryOperators">Count of binary operators (+ - * / % ^) in the source.</param>
/// <param name="FunctionCalls">Count of function calls (sqrt(...), ...).</param>
/// <param name="Operands">Count of numeric literals and named constants (pi, e).</param>
public readonly record struct CalcResult(double Value, int BinaryOperators, int FunctionCalls, int Operands);

/// <summary>
/// Pure, hand-written recursive-descent evaluator for the launcher's calculator row. Deliberately NOT
/// DataTable.Compute / a scripting engine: the input is whatever the user types into a search box, so the
/// grammar is closed and anything outside it is simply "not math" (returns null).
/// <code>
/// expr    := term   (('+' | '-') term)*
/// term    := unary  (('*' | '/' | '%') unary)*        // '%' is modulo, never "percent of"
/// unary   := ('-' | '+') unary | power
/// power   := primary ('^' unary)?                      // right-associative; -2^2 = -4, 2^-1 = 0.5
/// primary := number | constant | func '(' expr ')' | '(' expr ')'
/// number  := digits [('.' | ',') digits] | '.' digits | 0x hex | 0b binary
/// func    := sqrt abs round floor ceil sin cos tan log ln      constant := pi e
/// </code>
/// Comma rule: there are no multi-argument functions, so ',' is never an argument separator and is always
/// a decimal point (Turkish users type 3,5). Consequently "1,000" is 1.0, not one thousand - there is no
/// thousands-separator syntax. Trig works in radians; log is base 10, ln is natural; round is
/// away-from-zero at 0 decimals (what people expect, unlike banker's rounding). No implicit multiplication.
/// Any syntax error, unknown identifier, division/modulo by zero, domain error or non-finite result gives null.
/// </summary>
public static class CalcEvaluator
{
    private enum Kind { Number, Ident, Op, LParen, RParen, End }

    private readonly record struct Token(Kind Kind, string Text, double Number);

    public static CalcResult? Evaluate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 200)
        {
            return null;
        }

        var tokens = Tokenize(input);
        if (tokens is null)
        {
            return null;
        }

        try
        {
            var parser = new Parser(tokens);
            var value = parser.ParseAll();
            if (value is null || !double.IsFinite(value.Value))
            {
                return null;
            }

            // "+ 0.0" turns a negative zero into a plain zero.
            return new CalcResult(value.Value + 0.0, parser.BinaryOperators, parser.FunctionCalls, parser.Operands);
        }
        catch (CalcException)
        {
            return null;
        }
    }

    /// <summary>
    /// Display text: integers without decimals, otherwise up to 12 significant digits with a '.' decimal
    /// point (invariant on purpose: the same text is copied to the clipboard and pasted into code/Excel).
    /// </summary>
    public static string Format(double value)
    {
        if (value == 0)
        {
            return "0";
        }

        if (Math.Abs(value) < 1e15 && value == Math.Floor(value))
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        return value.ToString("G12", CultureInfo.InvariantCulture);
    }

    /// <summary>"0x30" for a non-negative integer result that fits in 63 bits; null otherwise.</summary>
    public static string? FormatHex(double value)
    {
        if (value < 0 || value >= 9.0e18 || value != Math.Floor(value))
        {
            return null;
        }

        return "0x" + ((long)value).ToString("X", CultureInfo.InvariantCulture);
    }

    private static List<Token>? Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '+' or '-' or '*' or '/' or '%' or '^')
            {
                tokens.Add(new Token(Kind.Op, c.ToString(), 0));
                i++;
            }
            else if (c == '(')
            {
                tokens.Add(new Token(Kind.LParen, "(", 0));
                i++;
            }
            else if (c == ')')
            {
                tokens.Add(new Token(Kind.RParen, ")", 0));
                i++;
            }
            else if (IsAsciiDigit(c) || (c is '.' or ',' && i + 1 < s.Length && IsAsciiDigit(s[i + 1])))
            {
                if (!ReadNumber(s, ref i, out var number))
                {
                    return null;
                }

                tokens.Add(new Token(Kind.Number, "", number));
                // "2pi", "2048game", "1.2.3": a number glued to a letter or another number is not math.
                if (i < s.Length && (IsAsciiLetter(s[i]) || IsAsciiDigit(s[i]) || s[i] is '.' or ','))
                {
                    return null;
                }
            }
            else if (IsAsciiLetter(c))
            {
                var start = i;
                while (i < s.Length && IsAsciiLetter(s[i]))
                {
                    i++;
                }

                tokens.Add(new Token(Kind.Ident, s[start..i].ToLowerInvariant(), 0));
            }
            else
            {
                return null;
            }
        }

        tokens.Add(new Token(Kind.End, "", 0));
        return tokens;
    }

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool ReadNumber(string s, ref int i, out double number)
    {
        number = 0;
        if (s[i] == '0' && i + 2 < s.Length && s[i + 1] is 'x' or 'X' or 'b' or 'B')
        {
            var hex = s[i + 1] is 'x' or 'X';
            var start = i + 2;
            var end = start;
            while (end < s.Length && (hex ? Uri.IsHexDigit(s[end]) : s[end] is '0' or '1'))
            {
                end++;
            }

            // 15 hex digits (60 bits) keeps the value exactly representable as a double.
            if (end == start || end - start > 15)
            {
                return false;
            }

            number = Convert.ToInt64(s[start..end], hex ? 16 : 2);
            i = end;
            return true;
        }

        var begin = i;
        while (i < s.Length && IsAsciiDigit(s[i]))
        {
            i++;
        }

        if (i < s.Length && s[i] is '.' or ',')
        {
            i++;
            while (i < s.Length && IsAsciiDigit(s[i]))
            {
                i++;
            }
        }

        // "5." / "5," with nothing after is accepted as 5; the comma is always a decimal point.
        var text = s[begin..i].Replace(',', '.');
        return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }

    private sealed class CalcException : Exception;

    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;
        private int _depth;

        public int BinaryOperators { get; private set; }

        public int FunctionCalls { get; private set; }

        public int Operands { get; private set; }

        private Token Current => tokens[_pos];

        public double? ParseAll()
        {
            var value = ParseExpr();
            return Current.Kind == Kind.End ? value : null;
        }

        private bool IsOp(string op) => Current.Kind == Kind.Op && Current.Text == op;

        private double ParseExpr()
        {
            var left = ParseTerm();
            while (IsOp("+") || IsOp("-"))
            {
                var plus = Current.Text == "+";
                _pos++;
                BinaryOperators++;
                var right = ParseTerm();
                left = plus ? left + right : left - right;
            }

            return left;
        }

        private double ParseTerm()
        {
            var left = ParseUnary();
            while (IsOp("*") || IsOp("/") || IsOp("%"))
            {
                var op = Current.Text;
                _pos++;
                BinaryOperators++;
                var right = ParseUnary();
                if (op != "*" && right == 0)
                {
                    throw new CalcException();
                }

                left = op switch
                {
                    "*" => left * right,
                    "/" => left / right,
                    _ => left % right,
                };
            }

            return left;
        }

        private double ParseUnary()
        {
            if (IsOp("-") || IsOp("+"))
            {
                var negative = Current.Text == "-";
                _pos++;
                Enter();
                var value = ParseUnary();
                _depth--;
                return negative ? -value : value;
            }

            return ParsePower();
        }

        private double ParsePower()
        {
            var baseValue = ParsePrimary();
            if (!IsOp("^"))
            {
                return baseValue;
            }

            _pos++;
            BinaryOperators++;
            Enter();
            var exponent = ParseUnary();
            _depth--;
            var result = Math.Pow(baseValue, exponent);
            return double.IsFinite(result) ? result : throw new CalcException();
        }

        private double ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case Kind.Number:
                    _pos++;
                    Operands++;
                    return token.Number;

                case Kind.LParen:
                {
                    _pos++;
                    Enter();
                    var value = ParseExpr();
                    _depth--;
                    Expect(Kind.RParen);
                    return value;
                }

                case Kind.Ident:
                {
                    _pos++;
                    if (token.Text == "pi")
                    {
                        Operands++;
                        return Math.PI;
                    }

                    if (token.Text == "e")
                    {
                        Operands++;
                        return Math.E;
                    }

                    if (Current.Kind != Kind.LParen)
                    {
                        throw new CalcException();
                    }

                    _pos++;
                    Enter();
                    var argument = ParseExpr();
                    _depth--;
                    Expect(Kind.RParen);
                    FunctionCalls++;
                    return Apply(token.Text, argument);
                }

                default:
                    throw new CalcException();
            }
        }

        private void Expect(Kind kind)
        {
            if (Current.Kind != kind)
            {
                throw new CalcException();
            }

            _pos++;
        }

        /// <summary>Bounds recursion so a pasted wall of parentheses cannot overflow the stack.</summary>
        private void Enter()
        {
            if (++_depth > 64)
            {
                throw new CalcException();
            }
        }

        private static double Apply(string name, double x)
        {
            var result = name switch
            {
                "sqrt" => x < 0 ? throw new CalcException() : Math.Sqrt(x),
                "abs" => Math.Abs(x),
                "round" => Math.Round(x, MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(x),
                "ceil" => Math.Ceiling(x),
                "sin" => Math.Sin(x),
                "cos" => Math.Cos(x),
                "tan" => Math.Tan(x),
                "log" => x <= 0 ? throw new CalcException() : Math.Log10(x),
                "ln" => x <= 0 ? throw new CalcException() : Math.Log(x),
                _ => throw new CalcException(),
            };
            return double.IsFinite(result) ? result : throw new CalcException();
        }
    }
}
