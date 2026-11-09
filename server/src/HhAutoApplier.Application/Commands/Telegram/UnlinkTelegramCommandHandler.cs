using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;
using Microsoft.EntityFrameworkCore;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Telegram;

public class UnlinkTelegramCommandHandler(IAppDbContext db, ICurrentUser current) : ICommandHandler<UnlinkTelegramCommand, UnlinkTelegramResult>
{
    public async Task<UnlinkTelegramResult> Handle(UnlinkTelegramCommand command, CancellationToken ct = default)
    {
        var chat = current.Id is { } userId ? await db.TelegramChats.FirstOrDefaultAsync(c => c.UserId == userId && c.ChatId != null, ct) : null;
        if (chat is null)
            return new NotFoundError("Чат Telegram не привязан");

        chat.Unlink();
        await db.SaveChangesAsync(ct);
        return new Success();
    }
}
