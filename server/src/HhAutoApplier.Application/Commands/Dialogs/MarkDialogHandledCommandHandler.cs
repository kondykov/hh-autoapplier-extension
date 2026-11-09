using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Dialogs;

public class MarkDialogHandledCommandHandler(
    IAppDbContext db,
    IExtractor<EmployerMessage, EmployerMessageDto> extractor,
    ICurrentUser current,
    TimeProvider clock
) : ICommandHandler<MarkDialogHandledCommand, MarkDialogHandledResult>
{
    public async Task<MarkDialogHandledResult> Handle(MarkDialogHandledCommand command, CancellationToken ct = default)
    {
        var message = await db.EmployerMessages.FirstOrDefaultAsync(m => m.UserId == current.Id && m.ChatId == command.ChatId, ct);
        if (message is null)
            return new NotFoundError($"Чат {command.ChatId} не найден");

        message.MarkHandled(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return extractor.Extract(message);
    }
}
