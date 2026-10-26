using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Telegram;

// Ручная проверка из чата: те же правила, что на /review.html (JobApplication.Approve/Decline), письмо — как написала LLM.
// Отклик ищется только среди откликов владельца привязанного чата
public class HandleTelegramCallbackCommandHandler(IAppDbContext db, IUserService users, TimeProvider clock)
    : ICommandHandler<HandleTelegramCallbackCommand, BotCallbackResult>
{
    public async Task<BotCallbackResult> Handle(HandleTelegramCallbackCommand command, CancellationToken ct = default)
    {
        var chat = await db.TelegramChats.AsNoTracking().FirstOrDefaultAsync(c => c.ChatId == command.ChatId, ct);
        if (chat is null || (await users.GetBlockedIdsAsync(ct)).Contains(chat.UserId))
            return new BotCallbackResult("Чат не привязан", null);

        var parts = command.Data.Split(':', 2);
        if (parts.Length != 2 || parts[0] is not (BotTexts.Approve or BotTexts.Decline))
            return new BotCallbackResult("Неизвестная кнопка", null);

        var application = await db.FindApplicationAsync(chat.UserId, parts[1], ct);
        if (application is null)
            return new BotCallbackResult("Вакансия не найдена", "Вакансия не найдена");

        try
        {
            if (parts[0] == BotTexts.Approve)
                application.Approve(null, clock.GetUtcNow());
            else
                application.Decline(null, clock.GetUtcNow());
        }
        catch (DomainException)
        {
            // Уже решено на странице проверки или другим нажатием — показать, что сейчас
            return new BotCallbackResult("Уже решено", $"Уже решено, статус: {application.Status}");
        }

        await db.SaveChangesAsync(ct);
        return parts[0] == BotTexts.Approve
            ? new BotCallbackResult("Одобрено", "✅ Одобрено — в очереди")
            : new BotCallbackResult("Отклонено", "❌ Отклонено");
    }
}
