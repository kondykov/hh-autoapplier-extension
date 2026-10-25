using HhAutoApplier.Domain.Services;

namespace HhAutoApplier.Domain.Vacancies;

// Агрегат: вакансия с агрегатора — общая карточка для всех пользователей. Оценка, письмо, анкета и статусы — в отклике (JobApplication)
public class Vacancy
{
    // Для EF
    private Vacancy()
    {
    }

    public string Id { get; private set; } = "";

    public string Title { get; private set; } = "";

    public string Company { get; private set; } = "";

    public string Url { get; private set; } = "";

    public string Area { get; private set; } = "";

    public string Description { get; private set; } = "";

    public int? SalaryFrom { get; private set; }

    public int? SalaryTo { get; private set; }

    public string? SalaryCurrency { get; private set; }

    public string WorkFormat { get; private set; } = "";

    public string[] Stack { get; private set; } = [];

    public bool HasTest { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }

    public DateTimeOffset CollectedAt { get; private set; }

    public string SearchName { get; private set; } = "";

    // Хранится тремя колонками, наружу — одним значением
    public Salary? Salary => Salary.From(SalaryFrom, SalaryTo, SalaryCurrency);

    public static Vacancy Create(string id, string title, string url, string searchName, DateTimeOffset collectedAt)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new DomainException("У вакансии нет идентификатора");

        return new Vacancy
        {
            Id = id,
            Title = title.Trim(),
            Url = url,
            SearchName = searchName,
            CollectedAt = collectedAt,
            PublishedAt = collectedAt,
        };
    }

    // Данные карточки: с выдачи источника или из импорта
    public void UpdateCard(string? company, string? area, Salary? salary, string? workFormat, bool? hasTest, DateTimeOffset? publishedAt)
    {
        if (!string.IsNullOrWhiteSpace(company))
            Company = company;

        if (!string.IsNullOrWhiteSpace(area))
            Area = area;

        if (salary is not null)
            (SalaryFrom, SalaryTo, SalaryCurrency) = (salary.Min, salary.Max, salary.Currency);

        if (!string.IsNullOrWhiteSpace(workFormat))
            WorkFormat = workFormat;

        if (hasTest is not null)
            HasTest = hasTest.Value;

        if (publishedAt is not null)
            PublishedAt = publishedAt.Value;
    }

    // Подробности со страницы вакансии; стек вычисляется по названию, описанию и навыкам
    public void UpdateDetails(string? title, string description, IEnumerable<string> skills)
    {
        if (!string.IsNullOrWhiteSpace(title))
            Title = title.Trim();

        Description = description.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description;
        Stack = StackExtractor.Extract($"{Title} {Description}", skills);
    }

    private const int MaxDescriptionLength = 4000;
}