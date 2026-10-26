using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Telegram;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Telegram;

public class LinkTelegramCommandHandler(IAppDbContext db, ICurrentUser current, ITelegramBot bot, TimeProvider clock)
    : ICommandHandler<LinkTelegramCommand, LinkTelegramResult>
{
    public async Task<LinkTelegramResult> Handle(LinkTelegramCommand command, CancellationToken ct = default)
    {
        if (current.ViaApiKey || current.Id is not { } userId)
            return new ConflictError("Код привязки выдаётся после входа по паролю");

        if (!bot.IsConfigured)
            return new ConflictError("Бот не настроен: задайте TELEGRAM_BOT_TOKEN");

        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (chat is null)
        {
            chat = TelegramChat.Create(userId);
            db.TelegramChats.Add(chat);
        }

        var code = chat.IssueCode(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        var name = await bot.GetUsernameAsync(ct);
        return new TelegramLinkDto(code, name is null ? null : $"https://t.me/{name}?start={code}", chat.CodeExpiresAt!.Value);
    }
}
