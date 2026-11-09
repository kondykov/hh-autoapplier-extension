using System.ComponentModel.DataAnnotations;

namespace HhAutoApplier.Application.Commands.Llm;

// Сбросить письма вакансий в очереди, чтобы LLM написала их заново: пустой список — все письма очереди пользователя
public record ResetCoverLettersCommand
{
    [MaxLength(200)]
    public List<string> VacancyIds { get; init; } = [];

    // Письма, поправленные человеком, тоже (#57); по умолчанию их не трогает
    public bool IncludeManual { get; init; }

    // Чьи письма (?userId, #57): по умолчанию — свои
    public Guid? UserId { get; init; }
}
