using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Resumes;
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

    // Резюме и профили кандидата — только владельцев откликов выборки: LLM-конвейер идёт раз в минуту
    public static IQueryable<ResumeProfile> ResumesOf(this IAppDbContext db, IEnumerable<JobApplication> applications)
    {
        var owners = Owners(applications);
        return db.Resumes.AsNoTracking().Where(r => owners.Contains(r.UserId));
    }

    public static IQueryable<CandidateProfile> ProfilesOf(this IAppDbContext db, IEnumerable<JobApplication> applications)
    {
        var owners = Owners(applications);
        return db.CandidateProfiles.AsNoTracking().Where(p => owners.Contains(p.UserId));
    }

    private static List<Guid?> Owners(IEnumerable<JobApplication> applications) =>
        applications.Select(a => a.UserId).Distinct().ToList();
}