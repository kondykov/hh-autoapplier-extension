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

    // Числа словами в заявлениях о сроках: «более трёх лет», «два года» (#67: модель пишет стаж словами, цифры такого не ловят)
    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["полтора"] = "1.5", ["полутора"] = "1.5",
        ["один"] = "1", ["одного"] = "1", ["одному"] = "1", ["одним"] = "1", ["одном"] = "1",
        ["два"] = "2", ["двух"] = "2", ["двум"] = "2", ["двумя"] = "2",
        ["три"] = "3", ["трёх"] = "3", ["трех"] = "3", ["трём"] = "3", ["трем"] = "3", ["тремя"] = "3",
        ["четыре"] = "4", ["четырёх"] = "4", ["четырех"] = "4", ["четырём"] = "4", ["четырем"] = "4", ["четырьмя"] = "4",
        ["пять"] = "5", ["пяти"] = "5", ["пятью"] = "5",
        ["шесть"] = "6", ["шести"] = "6", ["шестью"] = "6",
        ["семь"] = "7", ["семи"] = "7", ["семью"] = "7",
        ["восемь"] = "8", ["восьми"] = "8", ["восемью"] = "8",
        ["девять"] = "9", ["девяти"] = "9", ["девятью"] = "9",
        ["десять"] = "10", ["десяти"] = "10", ["десятью"] = "10",
        ["одиннадцать"] = "11", ["одиннадцати"] = "11",
        ["двенадцать"] = "12", ["двенадцати"] = "12",
        ["пятнадцать"] = "15", ["пятнадцати"] = "15",
        ["двадцать"] = "20", ["двадцати"] = "20",
    };

    // Срок: число или число словами и единица. «2020 года» — не срок: не больше двух цифр
    private static readonly Regex Duration = new(
        $@"(?<![\p{{L}}\d.,])(\d{{1,2}}(?:[.,]\d+)?|{string.Join("|", NumberWords.Keys)})\s*\+?\s+(лет|года|годам|годами|годах|годы|год|месяц\p{{L}}*)(?!\p{{L}})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

    // Цифры текста, которых нет в фактах в том же виде: неизвестное число или число с другой границей
    // («до 40 %» при «на 30–40%»). «Более N» — можно при «N+».
    // Сроки сверяются с candidateFacts (без него — с facts): срок из требований вакансии не подтверждает стаж кандидата
    public static IReadOnlyList<string> Unsupported(string text, string facts, string? candidateFacts = null)
    {
        var known = Figures(facts).ToHashSet();
        var bounded = Bounds(facts).ToHashSet();
        var unknown = Figures(text).Where(f => !known.Contains(f));
        var atLeast = AtLeast.Matches(facts).Select(m => Normalize(m.Groups[1].Value)).ToHashSet();
        var narrowed = Bounds(text).Where(b => !bounded.Contains(b) && !(b.Word is "более" or "свыше" or "больше" && atLeast.Contains(b.Figure)))
            .Select(b => $"{b.Word} {b.Figure}");
        var figures = unknown.Concat(narrowed).Distinct().ToList();

        var knownDurations = Durations(candidateFacts ?? facts).Select(d => d.Key).ToHashSet();
        var durations = Durations(text).Where(d => !knownDurations.Contains(d.Key) && !figures.Contains(d.Number)).Select(d => d.Text);
        return figures.Concat(durations).Distinct().ToList();
    }

    private static IEnumerable<(string Key, string Number, string Text)> Durations(string text) =>
        Duration.Matches(text).Select(m =>
        {
            var number = NumberWords.GetValueOrDefault(m.Groups[1].Value) ?? Normalize(m.Groups[1].Value);
            var unit = m.Groups[2].Value.StartsWith("месяц", StringComparison.OrdinalIgnoreCase) ? "мес" : "г";
            return ($"{number} {unit}", number, m.Value.ToLowerInvariant());
        });

    // Без пробелов (в том числе неразрывных), десятичная точка, дефисы и тире — «-»: «8 000» = «8000», «30 – 40» = «30-40»
    private static IEnumerable<string> Figures(string text) => Figure.Matches(text).Select(m => Normalize(m.Value));

    private static IEnumerable<(string Word, string Figure)> Bounds(string text) =>
        Bound.Matches(text).Select(m => (m.Groups[1].Value.ToLowerInvariant(), Normalize(m.Groups[2].Value)));

    private static string Normalize(string figure) =>
        new(figure.Where(c => !char.IsWhiteSpace(c)).Select(c => c switch { ',' => '.', >= '‐' and <= '—' => '-', _ => c }).ToArray());
}
