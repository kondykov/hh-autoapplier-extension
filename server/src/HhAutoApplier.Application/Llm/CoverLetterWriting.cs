using System.Text;
using System.Text.RegularExpressions;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Llm;

// Промпт сопроводительного письма под вакансию и очистка ответа. Чистые функции — без LLM и БД
public static class CoverLetterWriting
{
    public const string Purpose = "cover-letter";

    private const int MaxDescription = 3000;

    // gpt-oss сначала рассуждает: запас токенов, чтобы письмо не обрезалось
    private const int MaxTokens = 2000;

    // Короче — скорее отказ или мусор, чем письмо
    private const int MinLength = 40;

    // Причина в журнале LlmCalls, когда цифры письма не из фактов: по ней письмо вакансии не переписывается сутки
    public const string FiguresError = "цифры не из фактов";

    // Число или диапазон: «30–40», «8 000», «1.5», «300»; знак после (%, ×) не важен — сравниваются сами числа
    private const string Number = @"(?<![\p{L}\d.,])\d+(?:[   ]\d{3})*(?:[.,]\d+)?(?:\s*[-‐-—]\s*\d+(?:[.,]\d+)?)?";

    private static readonly Regex Figure = new(Number, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // Граница при числе: «до 30», «более 80» — смысл числа меняется вместе со словом
    private static readonly Regex Bound = new($@"(?<![\p{{L}}])(до|более|свыше|больше|почти)\s+({Number})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

    // «3+», «8 000+» — в фактах разрешает «более 3», «более 8 000»
    private static readonly Regex AtLeast = new($@"({Number})\s*\+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private const string System = """
        Ты помогаешь соискателю писать сопроводительные письма к откликам на hh.ru.
        Напиши письмо по-русски под конкретную вакансию: 3–6 предложений, до 900 знаков, деловой и живой тон без канцелярита.
        Упомяни 1–3 навыка или опыта кандидата из резюме, которые прямо отвечают требованиям вакансии, и назови компанию или роль.
        Строго только факты из резюме, профиля кандидата и шаблона — письмо уходит работодателю от имени кандидата (ниже «резюме» — это резюме вместе с профилем):
        - имя кандидата, подпись и контакты — только из шаблона, дословно; без шаблона — письмо без имени, без подписи и без контактов;
        - годы, стаж, проценты, сроки и другие цифры — дословно как в резюме или шаблоне, вместе со словом при них; иначе без цифр:
          диапазон пиши целиком («снизил на 30–40%», не «до 30%» и не «до 40%»), не добавляй к цифрам «до», «более», «свыше», «почти» («~80%» — «около 80%», не «более 80%»);
        - один проект, монолит или место работы не пиши во множественном числе и не обобщай («мигрировал монолит», не «мигрировал монолиты»);
        - навыки, технологии и проекты — только из резюме или шаблона; если требование вакансии в резюме не упомянуто, не приписывай его кандидату, а напиши об интересе к задаче;
        - не переноси требования вакансии в опыт кандидата: технологию или практику из вакансии, которой нет в резюме дословно (например, MVC, многопоточность, MS SQL), упоминай только как «готов освоить», не как опыт;
        - не называй места работы кандидата: пиши об опыте без привязки к компаниям, чтобы не перепутать, где что было; у должности без описанных задач не придумывай обязанности;
        - ключевые слова без описания резюме — это направления, а не подтверждённый опыт: не указывай по ним стаж и достижения.
        Если дан шаблон письма — сохрани его приветствие, стиль, подпись и контакты дословно, меняй только середину под вакансию.
        Ответь только текстом письма: без темы, заголовков, markdown, кавычек вокруг и заглушек вроде [Имя].
        """;

    // Низкая температура: письмо ближе к фактам резюме и шаблону
    public static LlmRequest BuildRequest(Vacancy vacancy, ResumeProfile? resume, CandidateProfile? profile = null) =>
        new([LlmMessage.System(System), LlmMessage.User(Describe(vacancy, resume, profile))], Temperature: 0.2, MaxTokens: MaxTokens);

    private static string Describe(Vacancy vacancy, ResumeProfile? resume, CandidateProfile? profile)
    {
        var text = new StringBuilder();
        if (CandidateContext.Describe(profile, $"{vacancy.Title}\n{vacancy.Description}", vacancy.Stack) is { } candidate)
            text.AppendLine(candidate).AppendLine();

        if (resume is not null)
        {
            text.AppendLine($"Резюме кандидата ({resume.Name}):")
                .AppendLine(resume.Summary
                            ?? $"подробного описания нет — опыт не известен; ключевые слова (направления, не стаж): {string.Join(", ", resume.Keywords)}");
            text.AppendLine().AppendLine(resume.LetterTemplate is { } template
                ? $"Шаблон письма кандидата:\n{template}"
                : "Шаблона письма нет — пиши без имени, подписи и контактов.");
        }
        else
        {
            text.AppendLine("Резюме кандидата: не выбрано — пиши без фактов о кандидате, без имени, подписи и контактов.");
        }

        var description = vacancy.Description.Length > MaxDescription ? vacancy.Description[..MaxDescription] + "…" : vacancy.Description;
        text.AppendLine()
            .AppendLine("Вакансия:")
            .AppendLine($"Название: {vacancy.Title}")
            .AppendLine($"Компания: {(vacancy.Company == "" ? "не указана" : vacancy.Company)}")
            .AppendLine($"Стек: {(vacancy.Stack.Length == 0 ? "не выделен" : string.Join(", ", vacancy.Stack))}")
            .AppendLine("Описание:")
            .Append(description);
        return text.ToString();
    }

    // Цифры письма, которых нет в промпте (резюме, профиль, шаблон, вакансия) в том же виде. Модель перефразирует цифры
    // вопреки промпту: «на 30–40%» превращается в «до 30 %» — такое письмо работодателю уходить не должно
    public static IReadOnlyList<string> UnsupportedFigures(string letter, LlmRequest request)
    {
        var facts = string.Join("\n", request.Messages.Where(m => m.Role == "user").Select(m => m.Content));
        var known = Figures(facts).ToHashSet();
        var bounded = Bounds(facts).ToHashSet();
        var unknown = Figures(letter).Where(f => !known.Contains(f));
        // Число есть в фактах, но про другое: «до 40 %» при «на 30–40%» и «latency −40%». «Более N» — можно при «N+»
        var atLeast = AtLeast.Matches(facts).Select(m => Normalize(m.Groups[1].Value)).ToHashSet();
        var narrowed = Bounds(letter).Where(b => !bounded.Contains(b) && !(b.Word is "более" or "свыше" or "больше" && atLeast.Contains(b.Figure)))
            .Select(b => $"{b.Word} {b.Figure}");
        return unknown.Concat(narrowed).Distinct().ToList();
    }

    // Повторная попытка: тот же разговор, письмо модели и список неверных цифр
    public static LlmRequest Retry(LlmRequest request, string letter, IReadOnlyList<string> figures) =>
        request with
        {
            Messages =
            [
                .. request.Messages,
                LlmMessage.Assistant(letter),
                LlmMessage.User($"В письме цифры, которых нет в резюме, профиле и шаблоне в таком виде: {string.Join(", ", figures)}. " +
                                "Перепиши письмо: цифры — только дословно как в фактах (диапазон целиком, без «до», «более», «свыше»), иначе без цифр."),
            ],
        };

    // Без пробелов (в том числе неразрывных), десятичная точка, дефисы и тире — «-»: «8 000» = «8000», «30 – 40» = «30-40»
    private static IEnumerable<string> Figures(string text) => Figure.Matches(text).Select(m => Normalize(m.Value));

    private static IEnumerable<(string Word, string Figure)> Bounds(string text) =>
        Bound.Matches(text).Select(m => (m.Groups[1].Value.ToLowerInvariant(), Normalize(m.Groups[2].Value)));

    private static string Normalize(string figure) =>
        new(figure.Where(c => !char.IsWhiteSpace(c)).Select(c => c switch { ',' => '.', >= '‐' and <= '—' => '-', _ => c }).ToArray());

    // null — ответ не похож на письмо (пустой, слишком короткий, JSON)
    public static string? Parse(string content)
    {
        var text = content.Trim();
        // Модель иногда оборачивает ответ в ```…``` или кавычки
        if (text.StartsWith("```"))
        {
            var firstLine = text.IndexOf('\n');
            text = firstLine < 0 ? "" : text[(firstLine + 1)..];
            if (text.EndsWith("```"))
                text = text[..^3];
            text = text.Trim();
        }

        if (text.Length > 1 && text[0] is '"' or '«' && text[^1] is '"' or '»')
            text = text[1..^1].Trim();

        if (text.Length < MinLength || text.StartsWith('{'))
            return null;

        return text.Replace("\r\n", "\n");
    }
}
