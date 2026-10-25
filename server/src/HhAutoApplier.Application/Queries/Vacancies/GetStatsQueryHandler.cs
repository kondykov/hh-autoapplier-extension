using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Vacancies;

public class GetStatsQueryHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetStatsQuery, GetStatsResult>
{
    public async Task<GetStatsResult> Handle(GetStatsQuery query, CancellationToken ct = default)
    {
        var scope = OwnerScope.Resolve(current, query.UserId);
        if (scope.IsT1)
            return scope.AsT1;

        var owner = scope.AsT0;
        var rows = await db.JobApplications.Where(a => a.UserId == owner).GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Status.ToString(), r => r.Count);
    }
}