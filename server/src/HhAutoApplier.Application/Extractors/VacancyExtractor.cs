using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Applications;

namespace HhAutoApplier.Application.Extractors;

// Ответ API прежний: карточка вакансии и отклик на неё одним объектом. Отклик загружается вместе с вакансией (Include)
public class VacancyExtractor : IExtractor<JobApplication, VacancyDto>
{
    public VacancyDto Extract(JobApplication source)
    {
        var vacancy = source.Vacancy;
        return new VacancyDto
        {
            Id = vacancy.Id,
            Title = vacancy.Title,
            Company = vacancy.Company,
            Url = vacancy.Url,
            Area = vacancy.Area,
            Description = vacancy.Description,
            SalaryFrom = vacancy.SalaryFrom,
            SalaryTo = vacancy.SalaryTo,
            SalaryCurrency = vacancy.SalaryCurrency,
            WorkFormat = vacancy.WorkFormat,
            Stack = vacancy.Stack,
            HasTest = vacancy.HasTest,
            PublishedAt = vacancy.PublishedAt,
            CollectedAt = vacancy.CollectedAt,
            SearchName = vacancy.SearchName,
            Status = source.Status,
            Score = source.Score,
            StatusReason = source.StatusReason,
            ResumeProfile = source.ResumeProfile,
            AppliedAt = source.AppliedAt,
            LlmVerdict = source.LlmVerdict,
            LlmScore = source.LlmScore,
            LlmReason = source.LlmReason,
            LlmModel = source.LlmModel,
            LlmEvaluatedAt = source.LlmEvaluatedAt,
            CoverLetter = source.CoverLetter,
            CoverLetterModel = source.CoverLetterModel,
            CoverLetterAt = source.CoverLetterAt,
            Questions = source.Questions.Select(q => new VacancyQuestionDto(q.Text, q.Type, q.Options, q.Answer)).ToList(),
            History = source.History.Select(e => new ApplicationEventDto(e.At, e.From, e.To, e.Reason)).ToList(),
        };
    }
}