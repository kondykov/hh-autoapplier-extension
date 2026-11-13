using HhAutoApplier.Domain.Applications;
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

    // Требуемый опыт текстом (#72); новое поле — прежние клиенты его не читают
    public string? WorkExperience { get; init; }

    public bool HasTest { get; init; }

    public DateTimeOffset PublishedAt { get; init; }

    public DateTimeOffset CollectedAt { get; init; }

    // Когда вакансия встретилась в последний раз (#70); новые поля — прежние клиенты их не читают
    public DateTimeOffset LastSeenAt { get; init; }

    public bool VacancyArchived { get; init; }

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

    // Скрыта из списка (#71)
    public DateTimeOffset? HiddenAt { get; init; }

    // Отсеяна человеком, LLM или правилами: можно оценить заново и скрыть (#71)
    public bool Dismissed { get; init; }

    // Просьбы работодателя к письму (#72); новые поля — прежние клиенты их не читают
    public LetterRequestsState LetterRequestsState { get; init; }

    public List<LetterRequestDto> LetterRequests { get; init; } = [];
}

// Text — цитата работодателя для показа; Value — выражение или кодовое слово; Answer — проверенное значение; Problem — почему не закрывается сама
public record LetterRequestDto(LetterRequestType Type, string Text, string? Value, string? Answer, bool Closed, string? Problem);
