using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Vacancies;

public class GetStatsQueryHandler(IAppDbContext db) : IQueryHandler<GetStatsQuery, IReadOnlyDictionary<string, int>>
{
    public async Task<IReadOnlyDictionary<string, int>> Handle(GetStatsQuery query, CancellationToken ct = default)
    {
        var rows = await db.Applications().GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Status.ToString(), r => r.Count);
    }
}
