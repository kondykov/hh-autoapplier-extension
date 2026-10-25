using System.ComponentModel.DataAnnotations;

namespace HhAutoApplier.Application.Commands.Llm;

// Сбросить письма вакансий в очереди, чтобы LLM написала их заново: пустой список — все письма очереди
public record ResetCoverLettersCommand
{
    [MaxLength(200)]
    public List<string> VacancyIds { get; init; } = [];
}
