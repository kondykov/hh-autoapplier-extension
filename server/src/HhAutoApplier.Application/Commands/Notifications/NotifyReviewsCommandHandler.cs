using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Notifications;

// Возвращает, сколько уведомлений записано. Ключ — отклик: повторный проход его не продублирует
public class NotifyReviewsCommandHandler(IAppDbContext db, ITelegramBot bot, Notifier notifier) : ICommandHandler<NotifyReviewsCommand, int>
{
    public async Task<int> Handle(NotifyReviewsCommand command, CancellationToken ct = default)
    {
        if (!bot.IsConfigured)
            return 0;

        var linked = await db.TelegramChats.Where(c => c.ChatId != null).Select(c => c.UserId).ToListAsync(ct);
        var waiting = await db.AllApplications().AsNoTracking()
            .Where(a => a.Status == VacancyStatus.Review && a.UserId != null && linked.Contains(a.UserId.Value))
            .OrderBy(a => a.Id).ToListAsync(ct);

        var written = 0;
        foreach (var application in waiting)
        {
            var message = BotTexts.Review(application);
            if (await notifier.NotifyAsync(application.UserId, NotificationKind.Review, $"review:{application.Id}", message.Text, application.VacancyId, ct))
                written++;
        }

        return written;
    }
}
