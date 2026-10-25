namespace HhAutoApplier.Domain.Searches;

// Правила отбора вакансий сохранённого поиска; хранится в БД JSON-ом
public class FilterRules
{
    public List<string> IncludeKeywords { get; init; } = [];

    public List<string> ExcludeKeywords { get; init; } = [];

    // Допустимые форматы работы (удалённо, гибрид, офис); пусто — любые
    public List<string> AllowedFormats { get; init; } = [];

    public int? MinSalaryRub { get; init; }

    public bool RequireSalary { get; init; }

    public bool SkipWithTest { get; init; }

    public int MinScore { get; init; }

    // Без ручной проверки: вакансии, в которых LLM сомневается, сразу идут в очередь (по умолчанию — на проверку, #25)
    public bool SkipReview { get; init; }
}
