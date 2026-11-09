using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Dialogs;

public class GetDialogsQueryHandler(
    IAppDbContext db,
    IExtractor<EmployerMessage, EmployerMessageDto> extractor,
    ICurrentUser current
) : IQueryHandler<GetDialogsQuery, GetDialogsResult>
{
    public async Task<GetDialogsResult> Handle(GetDialogsQuery query, CancellationToken ct = default)
    {
        var scope = OwnerScope.Resolve(current, query.UserId);
        if (scope.IsT1)
            return scope.AsT1;

        var owner = scope.AsT0;
        var rows = db.EmployerMessages.AsNoTracking().Where(m => m.UserId == owner);
        if (query.Kind is { } kind)
            rows = rows.Where(m => m.Kind == kind);
        if (query.Handled is { } handled)
            rows = rows.Where(m => (m.HandledAt != null) == handled);

        var list = await rows.OrderByDescending(m => m.At).Take(query.Take).ToListAsync(ct);
        return list.Select(extractor.Extract).ToList();
    }
}
