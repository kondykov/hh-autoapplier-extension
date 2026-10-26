using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Telegram;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Telegram;

// Команды бота: /start <код> привязывает чат, остальные работают только в привязанном чате незаблокированного пользователя
public class HandleTelegramMessageCommandHandler(IAppDbContext db, IUserService users, TimeProvider clock)
    : ICommandHandler<HandleTelegramMessageCommand, IReadOnlyList<BotMessage>>
{
    // Сколько откликов на проверке показать за раз — по сообщению на каждый
    private const int ReviewBatch = 10;

    public async Task<IReadOnlyList<BotMessage>> Handle(HandleTelegramMessageCommand command, CancellationToken ct = default)
    {
        if (!command.IsPrivate)
            return [];

        var parts = command.Text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // В группах Telegram дописывает имя бота: /status@my_bot
        var name = parts.Length > 0 ? parts[0].Split('@')[0].ToLowerInvariant() : "";
        var argument = parts.Length > 1 ? parts[1] : "";

        if (name == "/start" && argument != "")
            return [await LinkAsync(command, argument, ct)];

        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.ChatId == command.ChatId, ct);
        if (chat is null)
            return [new BotMessage(BotTexts.NotLinked)];

        if ((await users.GetBlockedIdsAsync(ct)).Contains(chat.UserId))
            return [new BotMessage("Учётная запись заблокирована")];

        switch (name)
        {
            case "/status":
                return [await StatusAsync(chat.UserId, ct)];
            case "/review":
                return await ReviewAsync(chat.UserId, ct);
            case "/stop":
                chat.Unlink();
                await db.SaveChangesAsync(ct);
                return [new BotMessage("Чат отвязан: уведомлений больше не будет. Привязать снова — новым кодом: /start <код>")];
            default:
                return [new BotMessage(BotTexts.Help)];
        }
    }

    private async Task<BotMessage> LinkAsync(HandleTelegramMessageCommand command, string code, CancellationToken ct)
    {
        var hash = TelegramChat.HashOf(code);
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.CodeHash == hash, ct);
        if (chat is null || (await users.GetBlockedIdsAsync(ct)).Contains(chat.UserId))
            return new BotMessage("Код не найден или устарел — получите новый");

        if (!chat.CodeValid(clock.GetUtcNow()))
            return new BotMessage("Код привязки устарел — получите новый");

        // Чат — у одного пользователя: прежняя привязка этого чата к другому снимается.
        // Отдельным сохранением до привязки: порядок UPDATE в одном сохранении не гарантирован, а ChatId уникален
        var others = await db.TelegramChats.Where(c => c.ChatId == command.ChatId && c.Id != chat.Id).ToListAsync(ct);
        if (others.Count > 0)
        {
            others.ForEach(o => o.Unlink());
            await db.SaveChangesAsync(ct);
        }

        chat.Link(command.ChatId, command.Username, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var user = await users.FindAsync(chat.UserId, ct);
        return new BotMessage($"Чат привязан к {user?.Email ?? "учётной записи"}.\n\n{BotTexts.Help}");
    }

    private async Task<BotMessage> StatusAsync(Guid userId, CancellationToken ct)
    {
        var counts = await db.JobApplications.Where(a => a.UserId == userId).GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, ct);
        var since = clock.GetUtcNow().AddDays(-1);
        var applied = await db.JobApplications.CountAsync(a => a.UserId == userId && a.AppliedAt >= since, ct);

        int Count(VacancyStatus status) => counts.GetValueOrDefault(status);
        return new BotMessage($"""
            В очереди: {Count(VacancyStatus.Queued)}
            На проверке: {Count(VacancyStatus.Review)}
            Анкеты ждут LLM: {Count(VacancyStatus.Questionnaire)}
            Ждут оценки LLM: {Count(VacancyStatus.Pending)}
            Откликов за сутки: {applied}, всего: {Count(VacancyStatus.Applied) + Count(VacancyStatus.Rejected)}
            """);
    }

    private async Task<IReadOnlyList<BotMessage>> ReviewAsync(Guid userId, CancellationToken ct)
    {
        var waiting = db.Applications(userId).Where(a => a.Status == VacancyStatus.Review);
        var total = await waiting.CountAsync(ct);
        if (total == 0)
            return [new BotMessage("Откликов на проверке нет")];

        var batch = await waiting.OrderByDescending(a => a.Score).Take(ReviewBatch).AsNoTracking().ToListAsync(ct);
        var messages = batch.Select(BotTexts.Review).ToList();
        if (total > batch.Count)
            messages.Add(new BotMessage($"Показано {batch.Count} из {total}: решите эти и отправьте /review ещё раз"));
        return messages;
    }
}
