using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Telegram;

public class GetTelegramQueryHandler(IAppDbContext db, ICurrentUser current, ITelegramBot bot) : IQueryHandler<GetTelegramQuery, TelegramChatDto>
{
    public async Task<TelegramChatDto> Handle(GetTelegramQuery query, CancellationToken ct = default)
    {
        var chat = current.Id is { } userId
            ? await db.TelegramChats.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.ChatId != null, ct)
            : null;
        return new TelegramChatDto(bot.IsConfigured, chat is not null, chat?.Username, chat?.LinkedAt);
    }
}
