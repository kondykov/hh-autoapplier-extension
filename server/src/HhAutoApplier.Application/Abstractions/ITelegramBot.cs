namespace HhAutoApplier.Application.Abstractions;

// Бот Telegram: реализация — в Infrastructure (Telegram.Bot). Без токена (TELEGRAM_BOT_TOKEN) IsConfigured = false:
// привязка чата недоступна, уведомления не создаются
public interface ITelegramBot
{
    bool IsConfigured { get; }

    // Имя бота (@username без @) — для ссылки привязки t.me/<имя>?start=<код>; null — Telegram не ответил
    Task<string?> GetUsernameAsync(CancellationToken ct = default);
}
