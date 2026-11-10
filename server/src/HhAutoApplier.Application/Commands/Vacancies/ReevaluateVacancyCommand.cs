using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Vacancies;

// Вернуть отсеянный отклик на оценку (#71)
public record ReevaluateVacancyCommand
{
    [Required]
    public required string VacancyId { get; init; }
}

// На .NET 11: public union ReevaluateVacancyResult(VacancyDto, NotFoundError, ConflictError, LlmUnavailableError);
[GenerateOneOf]
public partial class ReevaluateVacancyResult : OneOfBase<VacancyDto, NotFoundError, ConflictError, LlmUnavailableError>;
