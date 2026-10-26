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

    // Карточка пришла от клиента (импорт), а не от источника: карточка общая, поэтому первый же сбор,
    // встретивший вакансию, перезаписывает её данными источника — чужие правки не доходят до других пользователей
    public bool Imported { get; private set; }

    // Хранится тремя колонками, наружу — одним значением
    public Salary? Salary => Salary.From(SalaryFrom, SalaryTo, SalaryCurrency);

    public static Vacancy Create(string id, string title, string url, string searchName, DateTimeOffset collectedAt, bool imported = false)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new DomainException("У вакансии нет идентификатора");

        return new Vacancy
        {
            Id = id,
            Title = title.Trim(),
            Url = SafeUrl(id, url),
            SearchName = searchName,
            CollectedAt = collectedAt,
            PublishedAt = collectedAt,
            Imported = imported,
        };
    }

    // Источник подтвердил вакансию: карточка и подробности — целиком из источника (card уже с загруженными подробностями).
    // Флаг импорта снимается только вместе с заменой описания: иначе текст клиента остался бы в «подтверждённой» карточке
    public void AcceptSourceCard(Vacancy card)
    {
        if (card.Id != Id)
            throw new DomainException($"Карточка {card.Id} не от вакансии {Id}");

        (Title, Url, Company, Area, WorkFormat, HasTest, PublishedAt) =
            (card.Title, card.Url, card.Company, card.Area, card.WorkFormat, card.HasTest, card.PublishedAt);
        (SalaryFrom, SalaryTo, SalaryCurrency) = (card.SalaryFrom, card.SalaryTo, card.SalaryCurrency);
        (Description, Stack) = (card.Description, card.Stack);
        Imported = false;
    }

    // Ссылка открывается на страницах backend и в расширении: только https на hh.ru, иначе — стандартная страница вакансии
    // (javascript: и чужие сайты из импорта не проходят)
    private static string SafeUrl(string id, string? url)
    {
        var hh = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                 && (uri.Host == "hh.ru" || uri.Host.EndsWith(".hh.ru", StringComparison.Ordinal));
        return hh ? uri!.ToString() : $"https://hh.ru/vacancy/{Uri.EscapeDataString(id)}";
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