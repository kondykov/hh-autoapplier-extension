using System.ComponentModel.DataAnnotations;

namespace HhAutoApplier.Application.Queries.Vacancies;

// Очередь на отклик: лучшие вакансии с уже выбранным резюме
public record GetQueueQuery
{
    public string? Search { get; init; }

    public string? Resume { get; init; }

    [Range(1, 200)]
    public int Limit { get; init; } = 20;
}
