namespace HhAutoApplier.Domain.Searches;

// Агрегат: сохранённый поиск — что искать у источников и как отбирать найденное
public class SearchProfile
{
    // Для EF
    private SearchProfile()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    public string Query { get; private set; } = "";

    // Идентификатор региона hh.ru (например 89 — Тверь, 113 — Россия)
    public string? Area { get; private set; }

    // Дополнительные параметры страницы поиска hh.ru, например "work_format=REMOTE&experience=between1And3"
    public string? ExtraParams { get; private set; }

    public bool Enabled { get; private set; } = true;

    public FilterRules Rules { get; private set; } = new();

    public static SearchProfile Create(string name, string query, string? area, string? extraParams, FilterRules? rules, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("У поиска нет имени");

        var search = new SearchProfile { Name = name.Trim() };
        search.Update(query, area, extraParams, rules, enabled);
        return search;
    }

    public void Update(string query, string? area, string? extraParams, FilterRules? rules, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new DomainException("У поиска нет запроса");

        Query = query.Trim();
        Area = string.IsNullOrWhiteSpace(area) ? null : area.Trim();
        ExtraParams = string.IsNullOrWhiteSpace(extraParams) ? null : extraParams.Trim().TrimStart('&');
        Rules = rules ?? new FilterRules();
        Enabled = enabled;
    }
}
