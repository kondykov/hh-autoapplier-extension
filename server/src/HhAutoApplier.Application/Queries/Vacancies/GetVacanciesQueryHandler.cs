using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Vacancies;

public class GetVacanciesQueryHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    ICurrentUser current
) : IQueryHandler<GetVacanciesQuery, GetVacanciesResult>
{
    public async Task<GetVacanciesResult> Handle(GetVacanciesQuery query, CancellationToken ct = default)
    {
        var scope = OwnerScope.Resolve(current, query.UserId);
        if (scope.IsT1)
            return scope.AsT1;

        var q = db.Applications(scope.AsT0).AsNoTracking();
        if (query.Status is not null)
            q = q.Where(a => a.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.Search != null && a.Search.Name == query.Search);

        var rows = await q.OrderByDescending(a => a.Vacancy.PublishedAt)
            .Skip(Math.Max(query.Skip, 0)).Take(Math.Clamp(query.Take, 1, 500)).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}