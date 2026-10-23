using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Resumes;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Resumes;

public class GetResumesQueryHandler(
    IAppDbContext db,
    IExtractor<ResumeProfile, ResumeDto> extractor
) : IQueryHandler<GetResumesQuery, IReadOnlyList<ResumeDto>>
{
    public async Task<IReadOnlyList<ResumeDto>> Handle(GetResumesQuery query, CancellationToken ct = default)
    {
        var rows = await db.Resumes.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        return rows.Select(extractor.Extract).ToList();
    }
}
