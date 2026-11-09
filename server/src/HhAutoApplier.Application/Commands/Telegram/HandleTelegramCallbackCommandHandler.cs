using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Telegram;

// Кнопки под сообщениями бота. Ручная проверка — те же правила, что в веб-интерфейсе («Проверка») (JobApplication.Approve/Decline), письмо — как написала LLM.
// Отклики и ответы работодателей ищутся только среди данных владельца привязанного чата
public class HandleTelegramCallbackCommandHandler(
    IAppDbContext db,
    IUserService users,
    HandleTelegramMessageCommandHandler messages,
    TimeProvider clock
) : ICommandHandler<HandleTelegramCallbackCommand, BotCallbackResult>
{
    public async Task<BotCallbackResult> Handle(HandleTelegramCallbackCommand command, CancellationToken ct = default)
    {
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.ChatId == command.ChatId, ct);
        if (chat is null || (await users.GetBlockedIdsAsync(ct)).Contains(chat.UserId))
            return new BotCallbackResult("Чат не привязан", null);

        var parts = command.Data.Split(':', 2);
        if (parts.Length != 2)
            return new BotCallbackResult("Неизвестная кнопка", null);

        switch (parts[0])
        {
            case BotTexts.Approve or BotTexts.Decline:
                return await ReviewAsync(chat.UserId, parts[0] == BotTexts.Approve, parts[1], ct);
            case BotTexts.ShowReview:
                // Как команда /review: отклики на проверке новыми сообщениями, сообщение с кнопкой не меняется
                var replies = await messages.Handle(new HandleTelegramMessageCommand { ChatId = command.ChatId, IsPrivate = true, Text = "/review" }, ct);
                return new BotCallbackResult("", null) { Replies = replies };
            case BotTexts.Handled:
                return await HandledAsync(chat.UserId, parts[1], ct);
            case BotTexts.Unlink:
                chat.Unlink();
                await db.SaveChangesAsync(ct);
                return new BotCallbackResult("Чат отвязан", "Чат отвязан: уведомлений больше не будет. Привязать снова — новым кодом: /start <код>")
                {
                    Replies = [new BotMessage("Меню убрано.") { Menu = BotMenu.Remove }],
                };
            case BotTexts.Cancel:
                return new BotCallbackResult("Отменено", "Отменено");
            default:
                return new BotCallbackResult("Неизвестная кнопка", null);
        }
    }

    private async Task<BotCallbackResult> ReviewAsync(Guid userId, bool approve, string vacancyId, CancellationToken ct)
    {
        var application = await db.FindApplicationAsync(userId, vacancyId, ct);
        if (application is null)
            return new BotCallbackResult("Вакансия не найдена", "Вакансия не найдена");

        try
        {
            if (approve)
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
        return approve
            ? new BotCallbackResult("Одобрено", "✅ Одобрено — в очереди")
            : new BotCallbackResult("Отклонено", "❌ Отклонено");
    }

    // Как «Обработано» в веб-интерфейсе («Ответы работодателей»)
    private async Task<BotCallbackResult> HandledAsync(Guid userId, string chatId, CancellationToken ct)
    {
        var id = long.TryParse(chatId, out var parsed) ? parsed : 0;
        var message = await db.EmployerMessages.FirstOrDefaultAsync(m => m.UserId == userId && m.ChatId == id, ct);
        if (message is null)
            return new BotCallbackResult("Чат не найден", null);

        message.MarkHandled(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return new BotCallbackResult("Обработано", "✔ Обработано");
    }
}
