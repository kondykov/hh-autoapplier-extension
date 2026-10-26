using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Application.Notifications;

// Запись уведомлений в outbox: только тем, у кого привязан чат и кто не заблокирован, и только при настроенном боте —
// иначе их некому доставить. Повтор события с тем же ключом не создаёт второе сообщение.
// Вызывать после сохранения своих изменений сценария: уведомление сохраняется отдельно
public class Notifier(IAppDbContext db, IUserService users, ITelegramBot bot, TimeProvider clock, ILogger<Notifier> logger)
{
    // Возвращает, записано ли уведомление
    public async Task<bool> NotifyAsync(Guid? userId, NotificationKind kind, string key, string text, string? vacancyId = null, CancellationToken ct = default)
    {
        if (!bot.IsConfigured || userId is not { } id)
            return false;

        if (!await db.TelegramChats.AnyAsync(c => c.UserId == id && c.ChatId != null, ct))
            return false;

        if ((await users.GetBlockedIdsAsync(ct)).Contains(id))
            return false;

        if (await db.Notifications.AnyAsync(n => n.UserId == id && n.Key == key, ct))
            return false;

        var notification = Notification.Create(id, kind, key, text, clock.GetUtcNow(), vacancyId);
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException e)
        {
            // Тот же ключ успел записать параллельный запрос (уникальный индекс) — сообщение уже есть
            foreach (var entry in db.ChangeTracker.Entries().Where(x => ReferenceEquals(x.Entity, notification)))
                entry.State = EntityState.Detached;
            logger.LogDebug(e, "Уведомление {Key} уже записано", key);
            return false;
        }
    }

    // Сбои сервера — администраторам (незаблокированным, с привязанным чатом)
    public async Task NotifyAdminsAsync(NotificationKind kind, string key, string text, CancellationToken ct = default)
    {
        if (!bot.IsConfigured)
            return;

        foreach (var admin in (await users.GetAllAsync(ct)).Where(u => u.Role == Roles.Admin && !u.Blocked))
            await NotifyAsync(admin.Id, kind, key, text, null, ct);
    }
}
