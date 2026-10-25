using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Searches;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Searches;

public class GetSearchesQueryHandler(
    IAppDbContext db,
    IExtractor<SearchProfile, SearchDto> extractor
) : IQueryHandler<GetSearchesQuery, IReadOnlyList<SearchDto>>
{
    public async Task<IReadOnlyList<SearchDto>> Handle(GetSearchesQuery query, CancellationToken ct = default)
    {
        var rows = await db.Searches.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}
