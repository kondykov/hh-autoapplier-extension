using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;
using Microsoft.EntityFrameworkCore;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Searches;

public class DeleteSearchCommandHandler(IAppDbContext db) : ICommandHandler<DeleteSearchCommand, DeleteSearchResult>
{
    public async Task<DeleteSearchResult> Handle(DeleteSearchCommand command, CancellationToken ct = default)
    {
        var deleted = await db.Searches.Where(s => s.Id == command.Id).ExecuteDeleteAsync(ct);
        return deleted > 0 ? new Success() : new NotFoundError($"Поиск {command.Id} не найден");
    }
}
