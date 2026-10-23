using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;

namespace HhAutoApplier.Domain.Vacancies;

// Агрегат: вакансия с агрегатора, её оценка и путь по статусам до отклика
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

    public VacancyStatus Status { get; private set; }

    public int Score { get; private set; }

    public string? StatusReason { get; private set; }

    public string? ResumeProfile { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

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
            Status = VacancyStatus.New,
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

    public void Evaluate(VacancyEvaluator evaluator, FilterRules rules, IEnumerable<ResumeProfile> resumes)
    {
        if (Status is not (VacancyStatus.New or VacancyStatus.Queued or VacancyStatus.Skipped))
            throw new DomainException($"Вакансию в статусе {Status} нельзя переоценить");

        var evaluation = evaluator.Evaluate(this, rules, resumes);
        (Status, Score, StatusReason, ResumeProfile) =
            (evaluation.Status, evaluation.Score, evaluation.Reason, evaluation.ResumeProfile);
    }

    public void MarkApplied(string? resume, DateTimeOffset at)
    {
        if (Status is VacancyStatus.Applied or VacancyStatus.Rejected)
            throw new DomainException("Отклик на вакансию уже отправлен");

        Status = VacancyStatus.Applied;
        AppliedAt = at;
        StatusReason = null;
        if (!string.IsNullOrWhiteSpace(resume))
            ResumeProfile = resume;
    }

    public void Skip(string? reason)
    {
        if (Status is VacancyStatus.Applied or VacancyStatus.Rejected)
            throw new DomainException("Нельзя пропустить вакансию, на которую уже откликнулись");

        Status = VacancyStatus.Skipped;
        StatusReason = reason ?? StatusReason;
    }

    public void Fail(string? reason)
    {
        if (Status is VacancyStatus.Applied or VacancyStatus.Rejected)
            throw new DomainException("Отклик уже отправлен — ошибка отклика невозможна");

        Status = VacancyStatus.Failed;
        StatusReason = reason ?? StatusReason;
    }

    // Работодатель отказал после отклика
    public void Reject(string? reason)
    {
        if (Status != VacancyStatus.Applied)
            throw new DomainException("Отказ возможен только после отклика");

        Status = VacancyStatus.Rejected;
        StatusReason = reason ?? StatusReason;
    }

    // Вернуть в очередь пропущенную или упавшую вакансию
    public void Requeue()
    {
        if (Status is not (VacancyStatus.Skipped or VacancyStatus.Failed))
            throw new DomainException($"Из статуса {Status} в очередь вернуть нельзя");

        Status = VacancyStatus.Queued;
        StatusReason = null;
    }

    private const int MaxDescriptionLength = 4000;
}
