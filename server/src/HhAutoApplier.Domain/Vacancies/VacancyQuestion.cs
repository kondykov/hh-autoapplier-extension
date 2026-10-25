namespace HhAutoApplier.Domain.Vacancies;

// Тип вопроса анкеты работодателя: один вариант, несколько вариантов, свободный текст; Unknown — расширение не распознало поле
public enum QuestionType
{
    Radio = 0,
    Checkbox = 1,
    Text = 2,
    Unknown = 3,
}

// Вопрос анкеты работодателя и ответ на него; хранится в вакансии JSON-ом.
// Ответ: для Radio — один вариант, для Checkbox — один или несколько, для Text — одна строка; пусто — ответа нет
public class VacancyQuestion
{
    public string Text { get; init; } = "";

    public QuestionType Type { get; init; }

    public List<string> Options { get; init; } = [];

    public List<string> Answer { get; private set; } = [];

    public static VacancyQuestion Create(string text, QuestionType type, IEnumerable<string>? options)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("У вопроса анкеты нет текста");

        return new VacancyQuestion
        {
            Text = Collapse(text),
            Type = type,
            Options = (options ?? []).Select(Collapse).Where(o => o != "").ToList(),
        };
    }

    // Варианты сверяются без учёта регистра и лишних пробелов и сохраняются так, как написаны в анкете —
    // по этому тексту расширение найдёт их на странице
    public void SetAnswer(IReadOnlyCollection<string> answer)
    {
        var values = answer.Select(Collapse).Where(a => a != "").ToList();
        Answer = Type switch
        {
            QuestionType.Radio when values.Count == 1 => [MatchOption(values[0])],
            QuestionType.Checkbox when values.Count > 0 => values.Select(MatchOption).Distinct().ToList(),
            QuestionType.Text when values.Count > 0 => [string.Join("\n", values)],
            QuestionType.Unknown => throw new DomainException($"Поле анкеты не распознано: «{Text}»"),
            _ => throw new DomainException($"Нет ответа на вопрос «{Text}»"),
        };
    }

    private string MatchOption(string value) =>
        Options.FirstOrDefault(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException($"Варианта «{value}» нет в вопросе «{Text}»");

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
