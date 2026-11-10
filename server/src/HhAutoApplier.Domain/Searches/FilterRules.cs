namespace HhAutoApplier.Domain.Searches;

// Правила отбора вакансий сохранённого поиска; хранится в БД JSON-ом
public class FilterRules
{
    public List<string> IncludeKeywords { get; init; } = [];

    public List<string> ExcludeKeywords { get; init; } = [];

    // Слова уровня (junior, младший): ищутся только в названии и отсеивают лишь при явном признаке — без опыта или стажировка;
    // иначе вакансию оценивает LLM (#72). У поисков, сохранённых раньше, поля нет в JSON, и EF оставляет null
    public List<string> LevelKeywords { get => field ?? []; init; } = [];

    // Допустимые форматы работы (удалённо, гибрид, офис); пусто — любые
    public List<string> AllowedFormats { get; init; } = [];

    public int? MinSalaryRub { get; init; }

    public bool RequireSalary { get; init; }

    public bool SkipWithTest { get; init; }

    public int MinScore { get; init; }

    // Без ручной проверки: сомнительные вакансии и текстовые ответы анкеты сразу идут в очередь (по умолчанию — на проверку)
    public bool SkipReview { get; init; }
}
