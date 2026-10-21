namespace HhAutoApplier.Core.Domain;

public class FilterRules
{
    public List<string> IncludeKeywords { get; set; } = [];
    public List<string> ExcludeKeywords { get; set; } = [];
    // Допустимые форматы работы (удалённо, гибрид, офис); пусто — любые
    public List<string> AllowedFormats { get; set; } = [];
    public int? MinSalaryRub { get; set; }
    public bool RequireSalary { get; set; }
    public bool SkipWithTest { get; set; }
    public int MinScore { get; set; }
}

public class SearchProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Query { get; set; }
    // Идентификатор региона hh.ru (например 89 — Тверь, 113 — Россия)
    public string? Area { get; set; }
    // Дополнительные параметры API в виде query string, например "schedule=remote&experience=between1And3"
    public string? ExtraParams { get; set; }
    public bool Enabled { get; set; } = true;
    public FilterRules Rules { get; set; } = new();
}

public class ResumeProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    // Подстрока названия резюме на hh.ru
    public required string Match { get; set; }
    public List<string> Keywords { get; set; } = [];
}
