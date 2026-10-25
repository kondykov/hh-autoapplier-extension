using System.ComponentModel.DataAnnotations;

namespace HhAutoApplier.Application.Queries.Vacancies;

// Своя очередь на отклик: лучшие вакансии с уже выбранным резюме. Чужие очереди не отдаются даже администратору — их читает расширение
public record GetQueueQuery
{
    public string? Search { get; init; }

    public string? Resume { get; init; }

    [Range(1, 200)]
    public int Limit { get; init; } = 20;
}
