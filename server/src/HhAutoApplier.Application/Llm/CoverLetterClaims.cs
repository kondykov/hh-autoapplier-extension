using System.Text.RegularExpressions;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Llm;

// Что письмо заявляет от имени кандидата (#82): обещания, ответы на просьбы вакансии, технологии. Живой прогон показал,
// что такие запреты в промпте модель обходит. Чистые функции — без LLM и БД; сверка только с фактами о кандидате
public static class CoverLetterClaims
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    private const string SalaryWord = @"(?<!\p{L})(?:зарплат|оклад|заработн\p{L}*\s+плат|доход|компенсаци|вознагражден|зп(?!\p{L}))";

    private const string TestTask =
        @"(?<!\p{L})тестов\p{L}*\s+(?:\p{L}+\s+)?задани|(?<!\p{L})тестов\p{L}*\s+задач|(?<!\p{L})тестов(?:ое|ому|ым|ого)(?!\s+\p{L})" +
        @"|готов\p{L}*[^.!?;\n]{0,40}?тестов(?:ое|ому|ым)(?!\s+(?:окруж|покрыт|стенд|сред|контур|сценар|данн))|test\s+(?:task|assignment)";

    private const string Example =
        @"\d\s*=\s*\d|(?<!\p{L})равн[оаы]\s+\d|(?<!\p{L})(?:результат|решени|ответ)\p{L}*\s+(?:на\s+)?(?:пример|выражени|уравнени)\p{L}*(?!\p{L})";

    // Слово о зарплате рядом со словом о желании или обсуждении: «расчёт зарплаты» в описании опыта — не ответ на просьбу.
    // В фактах достаточно упоминания: «зарплата от 250 000» в профиле разрешает её назвать
    private static readonly (string Label, Regex Letter, Regex Facts)[] Promises_ =
    [
        Promise("готовность к тестовому заданию", TestTask, TestTask),
        Promise("зарплата",
            $@"(?:желаем|ожидаем|ожидани|обсу[дж]|обговор|претенд|рассчитыва)\p{{L}}*[^.!?;\n]{{0,60}}?{SalaryWord}" +
            $@"|{SalaryWord}\p{{L}}*[^.!?;\n]{{0,40}}?(?:желаем|ожидан|ожидаем|обсу[дж]|обговор|договор)",
            $@"{SalaryWord}|\d\s*(?:к|тыс\p{{L}}*|₽|руб\p{{L}}*)(?!\p{{L}})"),
        Promise("решение примера", Example, Example),
    ];

    private static (string, Regex, Regex) Promise(string label, string letter, string facts) =>
        (label, new Regex(letter, Options, Timeout), new Regex(facts, Options, Timeout));

    // Факт о языке или платформе следует из факта о фреймворке: «ASP.NET Core» подтверждает C#, письмо с этим законно
    private static readonly (string From, string To)[] Implied =
    [
        ("C#", ".NET"), (".NET", "C#"), ("Laravel", "PHP"), ("Symfony", "PHP"),
        ("TypeScript", "JavaScript"), ("React", "JavaScript"), ("Vue", "JavaScript"), ("Node.js", "JavaScript"),
    ];

    private static readonly Regex SentenceBreak = new(@"[.!?…;](?=\s|$)|\n", Options, Timeout);

    private static readonly Regex ClauseBreak = new(@"[.!?…;,](?=\s|$)|\n", Options, Timeout);

    // Намерение, а не опыт. Только инфинитив и настоящее: «изучил» — уже заявление об опыте
    private static readonly Regex Intent = new(
        @"(?<!\p{L})(?:изучить|изучать|изучу|изучени\p{L}*|освоить|осваивать|освою|освоени\p{L}*|научиться|разобраться|познакомиться|погрузиться|подтянуть|развиваться|интерес\p{L}*|хочу|хотел\p{L}*|планирую|стремлюсь)(?!\p{L})",
        Options, Timeout);

    // Честное «не работал»: рядом с ним технология кандидату не приписана
    private static readonly Regex Absence = new(
        @"(?<!\p{L})(?:не\s+(?:работал|имею|использовал|применял|сталкивал|знаком|доводилось|приходилось)\p{L}*|нет\s+(?:\p{L}+\s+){0,2}опыта|опыта\s+[^,.!?;\n]*?(?<!\p{L})нет)(?!\p{L})",
        Options, Timeout);

    // Заявление об опыте между намерением и технологией отменяет намерение: «хочу применить опыт с Docker»
    private static readonly Regex Experience = new(
        "опыт|навык|владе|работал|работаю|знаю|умею|использ|применял|применяю|разрабатыв|писал|пишу|настраивал|внедрял|сопровождал|поддерживал",
        Options, Timeout);

    private static readonly Regex Role = new(@"(?<!\p{L})(?:ваканси\p{L}*|позици\p{L}*|должност\p{L}*|рол[ьи])(?!\p{L})", Options, Timeout);

    private static readonly Regex LegalForm = new(@"^(?:ооо|оао|пао|зао|ао|нко|гк|ип)\s+", Options, Timeout);

    private static readonly Regex NameToken = new(@"(?<![\p{L}\d])[\p{L}\d]*\d[\p{L}\d]*(?![\p{L}\d])", Options, Timeout);

    // Технологии, в названии которых цифра стоит первой: сверка цифр видит в них число
    private static readonly Regex DigitTech = new(@"(?<![\p{L}\d])(?:1[cс]|[23]d|[45]g)(?![\p{L}\d])", Options, Timeout);

    private static readonly Regex Version = new(@"\G\s*\(?\d+(?:[.,]\d+)*\+?\)?", Options, Timeout);

    // Обещания и ответы на просьбы вакансии, которых нет в фактах о кандидате. Фраза из шаблона законна: шаблон входит в факты
    public static IReadOnlyList<string> Promises(string letter, string facts) =>
        Promises_.Where(p => p.Letter.IsMatch(letter) && !p.Facts.IsMatch(facts)).Select(p => p.Label).ToList();

    // Технологии словаря StackExtractor, которые письмо приписывает кандидату, а в фактах их нет.
    // Не приписывание: намерение («готов освоить Docker»), честное «не работал» и название компании или роли из вакансии
    public static IReadOnlyList<string> Technologies(string letter, string facts, Vacancy vacancy)
    {
        var known = StackExtractor.Extract(facts).ToHashSet();
        foreach (var (from, to) in Implied.Where(i => known.Contains(i.From)).ToList())
            known.Add(to);

        var text = WithoutNames(letter, vacancy);
        var inTitle = StackExtractor.Extract(vacancy.Title).ToHashSet();
        return StackExtractor.Find(text)
            .Where(t => !known.Contains(t.Name) && !Intended(text, t.Index) && !(inTitle.Contains(t.Name) && NamesRole(text, t.Index)))
            .OrderBy(t => t.Index).Select(t => t.Name).Distinct().ToList();
    }

    // Письмо без цифр, которые не заявляют ничего о кандидате: название компании и должности, «1С» и «2ГИС» из них,
    // технология с версией в контексте намерения. Версия при опыте остаётся: «ASP.NET Core (6+)» из требований — приписывание
    public static string WithoutFigureNames(string letter, Vacancy vacancy)
    {
        var text = WithoutNames(letter, vacancy);
        var tokens = new[] { vacancy.Company, vacancy.Title }.SelectMany(n => NameToken.Matches(n)).Select(m => m.Value)
            .Where(t => t.Any(char.IsLetter)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
            text = Bounded(string.Concat(token.Select(c => c is 'c' or 'C' or 'с' or 'С' ? "[cс]" : Regex.Escape(c.ToString())))).Replace(text, " ");

        // С конца: удаление не сдвигает позиции, которые ещё не обработаны
        var spans = StackExtractor.Find(text).Select(t => (t.Index, t.Length))
            .Concat(DigitTech.Matches(text).Select(m => (m.Index, m.Length)))
            .Where(s => Intended(text, s.Index)).OrderByDescending(s => s.Index).ToList();
        foreach (var (index, length) in spans)
        {
            var version = Version.Match(text, index + length);
            text = text[..index] + " " + text[(index + length + (version.Success ? version.Length : 0))..];
        }

        return text;
    }

    // Целиком названные компания и должность, в том числе компания без «ООО» и кавычек
    private static string WithoutNames(string letter, Vacancy vacancy)
    {
        var company = LegalForm.Replace(vacancy.Company.Trim(), "").Replace("«", "").Replace("»", "").Replace("\"", "").Trim();
        var names = new[] { vacancy.Company.Trim(), company, vacancy.Title.Trim() }.Where(n => n != "").Distinct().OrderByDescending(n => n.Length);
        foreach (var name in names)
            letter = Bounded(string.Join(@"\s+", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))).Replace(letter, " ");
        return letter;
    }

    private static Regex Bounded(string pattern) => new($@"(?<![\p{{L}}\d])(?:{pattern})(?![\p{{L}}\d])", Options, Timeout);

    private static bool Intended(string text, int index)
    {
        var (sentence, _) = Around(text, index, SentenceBreak);
        var (clause, clauseEnd) = Around(text, index, ClauseBreak);
        if (Absence.IsMatch(text[clause..clauseEnd]))
            return true;

        // Намерение до технологии — на всё предложение: «готов изучить Docker, Kubernetes и Kafka»; после — в той же части: «Docker хочу освоить»
        var before = text[sentence..index];
        if (Intent.Matches(before).LastOrDefault() is { } marker && !Experience.IsMatch(before[(marker.Index + marker.Length)..]))
            return true;
        return !Experience.IsMatch(text[clause..index]) && Intent.IsMatch(text[index..clauseEnd]);
    }

    // «Откликаюсь на вакансию разработчика Python»: роль названа в склонении, поэтому целиком название не совпадает
    private static bool NamesRole(string text, int index)
    {
        var (clause, _) = Around(text, index, ClauseBreak);
        var before = text[clause..index];
        return Role.Matches(before).LastOrDefault() is { } role && !Experience.IsMatch(before[(role.Index + role.Length)..]);
    }

    private static (int Start, int End) Around(string text, int index, Regex breaks)
    {
        var start = 0;
        foreach (Match m in breaks.Matches(text))
        {
            if (m.Index >= index)
                return (start, m.Index);
            start = m.Index + m.Length;
        }

        return (start, text.Length);
    }
}
