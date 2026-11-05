using System.Text.RegularExpressions;

namespace HhAutoApplier.Application.Llm;

// Сверка цифр текста LLM с фактами о кандидате: письмо и ответы анкеты уходят работодателю от имени кандидата.
// Модель перефразирует цифры вопреки промпту: «на 30–40%» превращается в «до 30 %» — такое уходить не должно
public static class FactFigures
{
    // Число или диапазон: «30–40», «8 000», «1.5», «300»; знак после (%, ×) не важен — сравниваются сами числа
    private const string Number = @"(?<![\p{L}\d.,])\d+(?:[   ]\d{3})*(?:[.,]\d+)?(?:\s*[-‐-—]\s*\d+(?:[.,]\d+)?)?";

    private static readonly Regex Figure = new(Number, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // Граница при числе: «до 30», «более 80» — смысл числа меняется вместе со словом
    private static readonly Regex Bound = new($@"(?<![\p{{L}}])(до|более|свыше|больше|почти)\s+({Number})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

    // «3+», «8 000+» — в фактах разрешает «более 3», «более 8 000»
    private static readonly Regex AtLeast = new($@"({Number})\s*\+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // Цифры текста, которых нет в фактах в том же виде: неизвестное число или число с другой границей
    // («до 40 %» при «на 30–40%»). «Более N» — можно при «N+»
    public static IReadOnlyList<string> Unsupported(string text, string facts)
    {
        var known = Figures(facts).ToHashSet();
        var bounded = Bounds(facts).ToHashSet();
        var unknown = Figures(text).Where(f => !known.Contains(f));
        var atLeast = AtLeast.Matches(facts).Select(m => Normalize(m.Groups[1].Value)).ToHashSet();
        var narrowed = Bounds(text).Where(b => !bounded.Contains(b) && !(b.Word is "более" or "свыше" or "больше" && atLeast.Contains(b.Figure)))
            .Select(b => $"{b.Word} {b.Figure}");
        return unknown.Concat(narrowed).Distinct().ToList();
    }

    // Без пробелов (в том числе неразрывных), десятичная точка, дефисы и тире — «-»: «8 000» = «8000», «30 – 40» = «30-40»
    private static IEnumerable<string> Figures(string text) => Figure.Matches(text).Select(m => Normalize(m.Value));

    private static IEnumerable<(string Word, string Figure)> Bounds(string text) =>
        Bound.Matches(text).Select(m => (m.Groups[1].Value.ToLowerInvariant(), Normalize(m.Groups[2].Value)));

    private static string Normalize(string figure) =>
        new(figure.Where(c => !char.IsWhiteSpace(c)).Select(c => c switch { ',' => '.', >= '‐' and <= '—' => '-', _ => c }).ToArray());
}
