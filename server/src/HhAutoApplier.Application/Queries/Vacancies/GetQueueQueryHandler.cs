using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Vacancies;

public class GetQueueQueryHandler(
    IAppDbContext db,
    IExtractor<Vacancy, VacancyDto> extractor
) : IQueryHandler<GetQueueQuery, IReadOnlyList<VacancyDto>>
{
    public async Task<IReadOnlyList<VacancyDto>> Handle(GetQueueQuery query, CancellationToken ct = default)
    {
        var q = db.Vacancies.AsNoTracking().Where(v => v.Status == VacancyStatus.Queued);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(v => v.SearchName == query.Search);

        if (!string.IsNullOrWhiteSpace(query.Resume))
            q = q.Where(v => v.ResumeProfile == query.Resume);

        var rows = await q.OrderByDescending(v => v.Score).ThenByDescending(v => v.PublishedAt)
            .Take(Math.Clamp(query.Limit, 1, 200)).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}
