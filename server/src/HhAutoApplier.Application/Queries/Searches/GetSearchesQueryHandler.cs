using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Searches;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Searches;

public class GetSearchesQueryHandler(
    IAppDbContext db,
    IExtractor<SearchProfile, SearchDto> extractor,
    ICurrentUser current
) : IQueryHandler<GetSearchesQuery, GetSearchesResult>
{
    public async Task<GetSearchesResult> Handle(GetSearchesQuery query, CancellationToken ct = default)
    {
        var scope = OwnerScope.Resolve(current, query.UserId);
        if (scope.IsT1)
            return scope.AsT1;

        var owner = scope.AsT0;
        var rows = await db.Searches.AsNoTracking().Where(s => s.UserId == owner).OrderBy(s => s.Name).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}