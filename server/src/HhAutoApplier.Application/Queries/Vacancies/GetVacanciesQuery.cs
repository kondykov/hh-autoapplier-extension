using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Queries.Vacancies;

public record GetVacanciesQuery
{
    public VacancyStatus? Status { get; init; }

    public string? Search { get; init; }

    [Range(0, int.MaxValue)]
    public int Skip { get; init; }

    [Range(1, 500)]
    public int Take { get; init; } = 50;
}
