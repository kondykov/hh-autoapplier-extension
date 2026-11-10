using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Vacancies;

// Скрыть отсеянный отклик из списка или вернуть его (#71)
public record SetVacancyHiddenCommand
{
    [Required]
    public required string VacancyId { get; init; }

    public required bool Hidden { get; init; }
}

// На .NET 11: public union SetVacancyHiddenResult(VacancyDto, NotFoundError, ConflictError);
[GenerateOneOf]
public partial class SetVacancyHiddenResult : OneOfBase<VacancyDto, NotFoundError, ConflictError>;
