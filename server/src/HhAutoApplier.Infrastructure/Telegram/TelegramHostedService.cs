using System.Net;
using HhAutoApplier.Application.Commands.Telegram;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace HhAutoApplier.Infrastructure.Telegram;

// Бот: приём сообщений и нажатий через long polling (сервер без адреса в интернете — вебхук не нужен)
// и доставка outbox уведомлений. Логика команд — в обработчиках Application, здесь только транспорт
public class TelegramHostedService(
    TelegramBot bot,
    IServiceScopeFactory scopes,
    IOptions<TelegramOptions> options,
    TimeProvider clock,
    ILogger<TelegramHostedService> logger
) : BackgroundService
{
    private static readonly TimeSpan ErrorPause = TimeSpan.FromSeconds(10);

    private static readonly LinkPreviewOptions NoPreview = new() { IsDisabled = true };

    protected override Task ExecuteAsync(CancellationToken stop)
    {
        if (bot.Client is not { } client)
        {
            logger.LogInformation("Telegram: бот выключен (нет TELEGRAM_BOT_TOKEN)");
            return Task.CompletedTask;
        }

        return Task.WhenAll(ReceiveAsync(client, stop), SendAsync(client, stop));
    }

    private async Task ReceiveAsync(ITelegramBotClient client, CancellationToken stop)
    {
        var timeout = Math.Clamp(options.Value.PollTimeoutSeconds, 1, 50);
        int? offset = null;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdates(offset, 100, timeout, [UpdateType.Message, UpdateType.CallbackQuery], stop);
                foreach (var update in updates)
                {
                    // Сдвиг до обработки: сообщение, уронившее обработчик, не будет приходить снова и снова
                    offset = update.Id + 1;
                    try
                    {
                        await HandleAsync(client, update, stop);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogError(e, "Telegram: обновление {Id} не обработано", update.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (ApiRequestException e)
            {
                // 409 — у бота webhook или тот же токен опрашивает другой экземпляр; 401 — неверный токен. Без стека: повторяется до исправления
                logger.LogWarning("Telegram: не удалось получить обновления — {Code} {Message}", e.ErrorCode, e.Message);
                await Task.Delay(ErrorPause, stop);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Telegram: не удалось получить обновления");
                await Task.Delay(ErrorPause, stop);
            }
        }
    }

    private async Task HandleAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (update.Message is { Text: { } text } message)
        {
            var handler = scope.ServiceProvider.GetRequiredService<HandleTelegramMessageCommandHandler>();
            var replies = await handler.Handle(new HandleTelegramMessageCommand
            {
                ChatId = message.Chat.Id,
                IsPrivate = message.Chat.Type == ChatType.Private,
                Text = text,
                Username = message.From?.Username,
            }, ct);
            foreach (var reply in replies)
                await client.SendMessage(message.Chat.Id, reply.Text, replyMarkup: Keyboard(reply.Buttons), linkPreviewOptions: NoPreview, cancellationToken: ct);
        }
        else if (update.CallbackQuery is { Data: { } data, Message: { } source } callback)
        {
            // Кнопки — только в личном чате с ботом: в группе нажать мог бы любой участник
            if (source.Chat.Type != ChatType.Private)
            {
                await client.AnswerCallbackQuery(callback.Id, "Только в личном чате", cancellationToken: ct);
                return;
            }

            var handler = scope.ServiceProvider.GetRequiredService<HandleTelegramCallbackCommandHandler>();
            var result = await handler.Handle(new HandleTelegramCallbackCommand { ChatId = source.Chat.Id, Data = data }, ct);
            await client.AnswerCallbackQuery(callback.Id, result.Notice, cancellationToken: ct);
            if (result.Status is not null)
                await client.EditMessageText(source.Chat.Id, source.MessageId, $"{source.Text}\n\n{result.Status}", linkPreviewOptions: NoPreview,
                    cancellationToken: ct);
        }
    }

    // Outbox: неотправленные уведомления — в привязанные чаты, по порядку записи
    private async Task SendAsync(ITelegramBotClient client, CancellationToken stop)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.SendIntervalSeconds, 1, 300));
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await SendPendingAsync(client, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Telegram: ошибка отправки уведомлений");
            }

            await Task.Delay(interval, stop);
        }
    }

    private async Task SendPendingAsync(ITelegramBotClient client, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.Notifications.Where(n => n.SentAt == null && n.Attempts < Notification.MaxAttempts)
            .OrderBy(n => n.Id).Take(20).ToListAsync(ct);
        if (pending.Count == 0)
            return;

        var userIds = pending.Select(n => n.UserId).Distinct().ToList();
        var chats = await db.TelegramChats.AsNoTracking().Where(c => userIds.Contains(c.UserId) && c.ChatId != null)
            .ToDictionaryAsync(c => c.UserId, c => c.ChatId!.Value, ct);

        foreach (var notification in pending)
        {
            if (!chats.TryGetValue(notification.UserId, out var chatId))
            {
                notification.Cancel("чат отвязан");
                continue;
            }

            var buttons = notification is { Kind: NotificationKind.Review, VacancyId: { } vacancyId } ? BotTexts.ReviewButtons(vacancyId) : [];
            try
            {
                await client.SendMessage(chatId, notification.Text, replyMarkup: Keyboard(buttons), linkPreviewOptions: NoPreview, cancellationToken: ct);
                notification.MarkSent(clock.GetUtcNow());
            }
            catch (ApiRequestException e) when (e.ErrorCode == (int)HttpStatusCode.Forbidden)
            {
                // Пользователь остановил бота — пока он не напишет боту снова, сообщения не дойдут
                notification.Cancel($"Telegram: {e.Message}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                notification.MarkFailed(e.Message);
                logger.LogWarning(e, "Telegram: уведомление {Id} не отправлено", notification.Id);
            }

            await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static InlineKeyboardMarkup? Keyboard(IReadOnlyList<BotButton> buttons) =>
        buttons.Count == 0 ? null : new InlineKeyboardMarkup(buttons.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.Data)));
}
