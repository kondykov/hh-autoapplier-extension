using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Vacancies;

public class GetVacanciesQueryHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor
) : IQueryHandler<GetVacanciesQuery, IReadOnlyList<VacancyDto>>
{
    public async Task<IReadOnlyList<VacancyDto>> Handle(GetVacanciesQuery query, CancellationToken ct = default)
    {
        var q = db.Applications().AsNoTracking();
        if (query.Status is not null)
            q = q.Where(a => a.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.Vacancy.SearchName == query.Search);

        var rows = await q.OrderByDescending(a => a.Vacancy.PublishedAt)
            .Skip(Math.Max(query.Skip, 0)).Take(Math.Clamp(query.Take, 1, 500)).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}