using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Candidates;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Candidates;

public class GetCandidateProfileQueryHandler(
    IAppDbContext db,
    IExtractor<CandidateProfile, CandidateProfileDto> extractor,
    ICurrentUser current
) : IQueryHandler<GetCandidateProfileQuery, GetCandidateProfileResult>
{
    public async Task<GetCandidateProfileResult> Handle(GetCandidateProfileQuery query, CancellationToken ct = default)
    {
        var scope = OwnerScope.Resolve(current, query.UserId);
        if (scope.IsT1)
            return scope.AsT1;

        var owner = scope.AsT0;
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == owner, ct);
        return profile is null ? new CandidateProfileDto(null, []) : extractor.Extract(profile);
    }
}
