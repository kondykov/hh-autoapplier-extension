using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Extractors;

public class VacancyExtractor : IExtractor<Vacancy, VacancyDto>
{
    public VacancyDto Extract(Vacancy source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Company = source.Company,
        Url = source.Url,
        Area = source.Area,
        Description = source.Description,
        SalaryFrom = source.SalaryFrom,
        SalaryTo = source.SalaryTo,
        SalaryCurrency = source.SalaryCurrency,
        WorkFormat = source.WorkFormat,
        Stack = source.Stack,
        HasTest = source.HasTest,
        PublishedAt = source.PublishedAt,
        CollectedAt = source.CollectedAt,
        SearchName = source.SearchName,
        Status = source.Status,
        Score = source.Score,
        StatusReason = source.StatusReason,
        ResumeProfile = source.ResumeProfile,
        AppliedAt = source.AppliedAt,
    };
}
