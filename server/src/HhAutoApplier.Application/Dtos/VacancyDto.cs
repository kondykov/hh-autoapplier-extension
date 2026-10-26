using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Dtos;

// Карточка вакансии и отклик на неё одним объектом: поля и имена совпадают с прежним ответом API — клиенты не ломаются
public record VacancyDto
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Company { get; init; }

    public required string Url { get; init; }

    public required string Area { get; init; }

    public required string Description { get; init; }

    public int? SalaryFrom { get; init; }

    public int? SalaryTo { get; init; }

    public string? SalaryCurrency { get; init; }

    public required string WorkFormat { get; init; }

    public required string[] Stack { get; init; }

    public bool HasTest { get; init; }

    public DateTimeOffset PublishedAt { get; init; }

    public DateTimeOffset CollectedAt { get; init; }

    public required string SearchName { get; init; }

    public VacancyStatus Status { get; init; }

    public int Score { get; init; }

    public string? StatusReason { get; init; }

    public string? ResumeProfile { get; init; }

    public DateTimeOffset? AppliedAt { get; init; }

    public LlmVerdict? LlmVerdict { get; init; }

    public int? LlmScore { get; init; }

    public string? LlmReason { get; init; }

    public string? LlmModel { get; init; }

    public DateTimeOffset? LlmEvaluatedAt { get; init; }

    public string? CoverLetter { get; init; }

    public string? CoverLetterModel { get; init; }

    public DateTimeOffset? CoverLetterAt { get; init; }

    public List<VacancyQuestionDto> Questions { get; init; } = [];

    // История статусов отклика (#8); новое поле — прежние клиенты его не читают
    public List<ApplicationEventDto> History { get; init; } = [];
}
