using HhAutoApplier.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Abstractions;

// Отклики вместе с карточкой вакансии — так их читают все сценарии.
// Пока у поисков нет владельцев (#9), все отклики — владельца по умолчанию (UserId = null); в #9 фильтр станет текущим пользователем
public static class JobApplicationQueries
{
    public static IQueryable<JobApplication> Applications(this IAppDbContext db) =>
        db.JobApplications.Include(a => a.Vacancy).Where(a => a.UserId == null);

    public static Task<JobApplication?> FindApplicationAsync(this IAppDbContext db, string vacancyId, CancellationToken ct) =>
        db.Applications().FirstOrDefaultAsync(a => a.VacancyId == vacancyId, ct);
}