using HhAutoApplier.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Abstractions;

// Отклики вместе с карточкой вакансии и поиском — так их читают все сценарии
public static class JobApplicationQueries
{
    // Все пользователи — для фоновых проходов (сбор, LLM)
    public static IQueryable<JobApplication> AllApplications(this IAppDbContext db) =>
        db.JobApplications.Include(a => a.Vacancy).Include(a => a.Search);

    // Фоновые проходы: все, кроме заблокированных пользователей
    public static IQueryable<JobApplication> ActiveApplications(this IAppDbContext db, IReadOnlyList<Guid> blocked) =>
        db.AllApplications().Where(a => a.UserId == null || !blocked.Contains(a.UserId.Value));

    // Отклики владельца; owner = null — владелец по умолчанию
    public static IQueryable<JobApplication> Applications(this IAppDbContext db, Guid? owner) =>
        db.AllApplications().Where(a => a.UserId == owner);

    public static Task<JobApplication?> FindApplicationAsync(this IAppDbContext db, Guid? owner, string vacancyId, CancellationToken ct) =>
        db.Applications(owner).FirstOrDefaultAsync(a => a.VacancyId == vacancyId, ct);
}