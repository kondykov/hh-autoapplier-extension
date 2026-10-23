using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Vacancies;

public class GetVacanciesQueryHandler(
    IAppDbContext db,
    IExtractor<Vacancy, VacancyDto> extractor
) : IQueryHandler<GetVacanciesQuery, IReadOnlyList<VacancyDto>>
{
    public async Task<IReadOnlyList<VacancyDto>> Handle(GetVacanciesQuery query, CancellationToken ct = default)
    {
        var q = db.Vacancies.AsNoTracking();
        if (query.Status is not null)
            q = q.Where(v => v.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(v => v.SearchName == query.Search);

        var rows = await q.OrderByDescending(v => v.PublishedAt)
            .Skip(Math.Max(query.Skip, 0)).Take(Math.Clamp(query.Take, 1, 500)).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}
