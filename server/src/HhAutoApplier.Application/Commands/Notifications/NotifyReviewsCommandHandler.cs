using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Notifications;

// Возвращает, сколько уведомлений записано. Ключ — отклик: повторный проход его не продублирует.
// Больше трёх новых откликов на проверке за проход — одна сводка (решение владельца 2026-10-28: 18 сообщений разом — шум);
// отклики сводки помечаются отменёнными уведомлениями, чтобы не прийти потом по одному
public class NotifyReviewsCommandHandler(IAppDbContext db, ITelegramBot bot, Notifier notifier, TimeProvider clock) : ICommandHandler<NotifyReviewsCommand, int>
{
    public const int DigestFrom = 4;

    public async Task<int> Handle(NotifyReviewsCommand command, CancellationToken ct = default)
    {
        if (!bot.IsConfigured)
            return 0;

        var linked = await db.TelegramChats.Where(c => c.ChatId != null).Select(c => c.UserId).ToListAsync(ct);
        var waiting = await db.AllApplications().AsNoTracking()
            .Where(a => a.Status == VacancyStatus.Review && a.UserId != null && linked.Contains(a.UserId.Value))
            .OrderBy(a => a.Id).ToListAsync(ct);
        var known = (await db.Notifications.Where(n => n.Kind == NotificationKind.Review && linked.Contains(n.UserId))
            .Select(n => n.Key).ToListAsync(ct)).ToHashSet();

        var written = 0;
        foreach (var user in waiting.GroupBy(a => a.UserId!.Value))
        {
            var fresh = user.Where(a => !known.Contains(ReviewKey(a.Id))).ToList();
            if (fresh.Count < DigestFrom)
            {
                foreach (var application in fresh)
                {
                    var message = BotTexts.Review(application);
                    if (await notifier.NotifyAsync(application.UserId, NotificationKind.Review, ReviewKey(application.Id), message.Text, application.VacancyId, ct))
                        written++;
                }

                continue;
            }

            if (!await notifier.NotifyAsync(user.Key, NotificationKind.Review, $"review-digest:{fresh.Max(a => a.Id)}",
                    BotTexts.ReviewDigest(fresh, user.Count()), null, ct))
                continue;

            written++;
            foreach (var application in fresh)
            {
                var covered = Notification.Create(user.Key, NotificationKind.Review, ReviewKey(application.Id), "в сводке", clock.GetUtcNow(), application.VacancyId);
                covered.Cancel("в сводке");
                db.Notifications.Add(covered);
            }

            await db.SaveChangesAsync(ct);
        }

        return written;
    }

    private static string ReviewKey(int applicationId) => $"review:{applicationId}";
}
