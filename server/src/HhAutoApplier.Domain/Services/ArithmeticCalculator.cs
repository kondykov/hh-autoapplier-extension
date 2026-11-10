using System.Globalization;

namespace HhAutoApplier.Domain.Services;

// Пример из вакансии (#72): только числа и + - * / ( ), длина ограничена. Отказ — null: на проверку идёт человек, сервер не догадывается
public static class ArithmeticCalculator
{
    public const int MaxLength = 64;

    private const int MaxDigits = 15;

    // Знаков после запятой в ответе: 1/3 точно не записать, такой пример решает человек
    private const int MaxScale = 6;

    public static ArithmeticResult? Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxLength || Tokenize(expression) is not { } tokens)
            return null;

        try
        {
            var parser = new Parser(tokens);
            var value = parser.Parse();
            if (value is null || Math.Round(value.Value, MaxScale) != value.Value)
                return null;

            return new ArithmeticResult(parser.Rendered, value.Value.ToString("0.######", CultureInfo.InvariantCulture));
        }
        catch (DivideByZeroException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static List<string>? Tokenize(string text)
    {
        var tokens = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (c == ' ')
            {
                i++;
            }
            else if (c is '+' or '-' or '*' or '/' or '(' or ')')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else if (char.IsAsciiDigit(c))
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                    i++;
                var number = text[start..i];
                if (number.Count(char.IsAsciiDigit) > MaxDigits || number.Count(d => d == '.') > 1 || number.EndsWith('.'))
                    return null;
                tokens.Add(number);
            }
            else
            {
                return null;
            }
        }

        return tokens;
    }

    private sealed class Parser(List<string> tokens)
    {
        private int _position;

        // Хотя бы одна операция: голое число — не пример (телефон из текста вакансии так не пройдёт)
        private bool _hasOperation;

        private string? Current => _position < tokens.Count ? tokens[_position] : null;

        // Запись собрана из токенов, а не взята из текста работодателя
        public string Rendered => string.Concat(tokens.Select(Spaced)).Trim();

        public decimal? Parse()
        {
            var value = Sum();
            return value is null || _position != tokens.Count || !_hasOperation ? null : value;
        }

        private string Spaced(string token, int index)
        {
            var binary = token is "+" or "*" or "/" || (token == "-" && index > 0 && tokens[index - 1] is not ("(" or "+" or "-" or "*" or "/"));
            return binary ? $" {token} " : token;
        }

        private decimal? Sum()
        {
            var value = Product();
            while (value is not null && Current is "+" or "-")
            {
                var op = tokens[_position++];
                _hasOperation = true;
                if (Product() is not { } right)
                    return null;
                value = op == "+" ? value + right : value - right;
            }

            return value;
        }

        private decimal? Product()
        {
            var value = Factor();
            while (value is not null && Current is "*" or "/")
            {
                var op = tokens[_position++];
                _hasOperation = true;
                if (Factor() is not { } right)
                    return null;
                value = op == "*" ? value * right : value / right;
            }

            return value;
        }

        private decimal? Factor()
        {
            var negative = Current == "-";
            if (negative)
                _position++;

            decimal value;
            if (Current == "(")
            {
                _position++;
                if (Sum() is not { } inner || Current != ")")
                    return null;
                value = inner;
                _position++;
            }
            else if (Current is { } number && char.IsAsciiDigit(number[0]))
            {
                value = decimal.Parse(number, CultureInfo.InvariantCulture);
                _position++;
            }
            else
            {
                return null;
            }

            return negative ? -value : value;
        }
    }
}

public record ArithmeticResult(string Expression, string Value);
