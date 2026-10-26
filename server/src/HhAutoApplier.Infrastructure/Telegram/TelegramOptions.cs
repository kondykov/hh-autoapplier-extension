namespace HhAutoApplier.Infrastructure.Telegram;

public class TelegramOptions
{
    public const string Section = "Telegram";

    // Токен от @BotFather, только из .env (TELEGRAM_BOT_TOKEN); пустой — бот выключен
    public string? BotToken { get; set; }

    // Long polling: Telegram держит запрос до появления сообщений, но не дольше этого
    public int PollTimeoutSeconds { get; set; } = 30;

    // Как часто проверять outbox уведомлений
    public int SendIntervalSeconds { get; set; } = 5;
}
