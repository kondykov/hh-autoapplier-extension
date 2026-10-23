namespace HhAutoApplier.Domain.Resumes;

// Агрегат: профиль резюме — как найти резюме на hh.ru и по каким словам выбирать его для вакансии
public class ResumeProfile
{
    // Для EF
    private ResumeProfile()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    // Подстрока названия резюме на hh.ru
    public string Match { get; private set; } = "";

    public string[] Keywords { get; private set; } = [];

    public static ResumeProfile Create(string name, string match, IEnumerable<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("У резюме нет имени");

        var resume = new ResumeProfile { Name = name.Trim() };
        resume.Update(match, keywords);
        return resume;
    }

    public void Update(string match, IEnumerable<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(match))
            throw new DomainException("Не задана подстрока для поиска резюме на hh.ru");

        Match = match.Trim();
        Keywords = keywords.Select(k => k.Trim()).Where(k => k != "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
