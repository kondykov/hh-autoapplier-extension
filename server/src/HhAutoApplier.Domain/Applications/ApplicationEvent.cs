using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Domain.Applications;

// Переход отклика между статусами; история хранится в отклике JSON-ом. From = null — отклик создан
public class ApplicationEvent
{
    public DateTimeOffset At { get; init; }

    public VacancyStatus? From { get; init; }

    public VacancyStatus To { get; init; }

    public string? Reason { get; init; }
}