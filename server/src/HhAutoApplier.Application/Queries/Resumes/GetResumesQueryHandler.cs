using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Resumes;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Resumes;

public class GetResumesQueryHandler(
    IAppDbContext db,
    IExtractor<ResumeProfile, ResumeDto> extractor,
    ICurrentUser current
) : IQueryHandler<GetResumesQuery, GetResumesResult>
{
    public async Task<GetResumesResult> Handle(GetResumesQuery query, CancellationToken ct = default)
    {
        var scope = OwnerScope.Resolve(current, query.UserId);
        if (scope.IsT1)
            return scope.AsT1;

        var owner = scope.AsT0;
        var rows = await db.Resumes.AsNoTracking().Where(r => r.UserId == owner).OrderBy(r => r.Name).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}