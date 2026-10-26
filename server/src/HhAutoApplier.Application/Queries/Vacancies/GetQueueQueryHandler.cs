using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Vacancies;

public class GetQueueQueryHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    ICurrentUser current
) : IQueryHandler<GetQueueQuery, IReadOnlyList<VacancyDto>>
{
    public async Task<IReadOnlyList<VacancyDto>> Handle(GetQueueQuery query, CancellationToken ct = default)
    {
        var q = db.Applications(current.Id).AsNoTracking().Where(a => a.Status == VacancyStatus.Queued);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.Search != null && a.Search.Name == query.Search);

        if (!string.IsNullOrWhiteSpace(query.Resume))
            q = q.Where(a => a.ResumeProfile == query.Resume);

        var rows = await q.OrderByDescending(a => a.Score).ThenByDescending(a => a.Vacancy.PublishedAt)
            .Take(Math.Clamp(query.Limit, 1, 200)).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}