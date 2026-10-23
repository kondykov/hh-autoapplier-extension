namespace HhAutoApplier.Domain.Vacancies;

public enum VacancyStatus
{
    New = 0,
    Queued = 1,
    Applied = 2,
    Skipped = 3,
    Rejected = 4,
    Failed = 5,
}
