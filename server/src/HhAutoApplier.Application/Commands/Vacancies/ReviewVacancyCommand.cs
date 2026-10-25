using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Vacancies;

// Ручная проверка сомнительной вакансии (Review): одобрить — в очередь, при желании с поправленным письмом; отклонить — пропуск
public record ReviewVacancyCommand
{
    [Required]
    public required string VacancyId { get; init; }

    public bool Approve { get; init; }

    // Одобрить: письмо, поправленное человеком; пусто — оставить письмо LLM
    [MaxLength(4000)]
    public string? CoverLetter { get; init; }

    // Отклонить: причина для отчёта
    [MaxLength(500)]
    public string? Reason { get; init; }
}

// На .NET 11: public union ReviewVacancyResult(VacancyDto, NotFoundError, ConflictError);
[GenerateOneOf]
public partial class ReviewVacancyResult : OneOfBase<VacancyDto, NotFoundError, ConflictError>;
