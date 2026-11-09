using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Dialogs;

public class ReportAutoReplyCommandHandler(
    IAppDbContext db,
    IExtractor<EmployerMessage, EmployerMessageDto> extractor,
    ICurrentUser current,
    TimeProvider clock
) : ICommandHandler<ReportAutoReplyCommand, ReportAutoReplyResult>
{
    public async Task<ReportAutoReplyResult> Handle(ReportAutoReplyCommand command, CancellationToken ct = default)
    {
        var message = await db.EmployerMessages.FirstOrDefaultAsync(m => m.UserId == current.Id && m.ChatId == command.ChatId, ct);
        if (message is null)
            return new NotFoundError($"Чат {command.ChatId} не найден");

        try
        {
            message.ReportAutoReply(command.Sent, command.Error, clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(message);
    }
}
