using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Vacancies;
using OneOf;

namespace HhAutoApplier.Application.Queries.Vacancies;

public record GetVacanciesQuery
{
    public VacancyStatus? Status { get; init; }

    public string? Search { get; init; }

    [Range(0, int.MaxValue)]
    public int Skip { get; init; }

    [Range(1, 500)]
    public int Take { get; init; } = 50;

    // Администратор — отклики этого пользователя; без него — свои
    public Guid? UserId { get; init; }
}

// На .NET 11: public union GetVacanciesResult(List<VacancyDto>, ForbiddenError);
[GenerateOneOf]
public partial class GetVacanciesResult : OneOfBase<List<VacancyDto>, ForbiddenError>;