using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Tokens;

public class GetApiTokensQueryHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetApiTokensQuery, IReadOnlyList<ApiTokenDto>>
{
    public async Task<IReadOnlyList<ApiTokenDto>> Handle(GetApiTokensQuery query, CancellationToken ct = default) =>
        await db.ApiTokens.AsNoTracking().Where(t => t.UserId == current.Id).OrderByDescending(t => t.CreatedAt)
            .Select(t => new ApiTokenDto(t.Id, t.Name, t.Prefix, t.CreatedAt, t.RevokedAt)).ToListAsync(ct);
}