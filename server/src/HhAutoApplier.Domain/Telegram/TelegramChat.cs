using System.Security.Cryptography;
using System.Text;

namespace HhAutoApplier.Domain.Telegram;

// Агрегат: чат Telegram пользователя — куда бот шлёт уведомления и откуда принимает команды. У пользователя один чат.
// Привязка — одноразовым кодом: пользователь получает его после входа по паролю и отправляет боту /start <код>.
// В базе только хэш кода; новый код не отвязывает прежний чат, пока не привяжется новый
public class TelegramChat
{
    // Для EF
    private TelegramChat()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    // Личный чат с ботом; null — ещё не привязан
    public long? ChatId { get; private set; }

    // Имя в Telegram (@username) — чтобы узнать чат в настройках
    public string? Username { get; private set; }

    public DateTimeOffset? LinkedAt { get; private set; }

    // SHA-256 кода привязки; null — кода нет или уже использован
    public string? CodeHash { get; private set; }

    public DateTimeOffset? CodeExpiresAt { get; private set; }

    public bool Linked => ChatId is not null;

    public static TelegramChat Create(Guid userId) => new() { Id = Guid.NewGuid(), UserId = userId };

    // Новый код привязки (открытое значение — только сейчас); прежний код перестаёт действовать
    public string IssueCode(DateTimeOffset at)
    {
        // Код идёт в ссылку t.me/<бот>?start=<код>: там допустимы только A-Z, a-z, 0-9, _ и -, не длиннее 64 символов
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');
        (CodeHash, CodeExpiresAt) = (HashOf(code), at + CodeLifetime);
        return code;
    }

    public bool CodeValid(DateTimeOffset at) => CodeHash is not null && CodeExpiresAt is { } expires && at <= expires;

    public void Link(long chatId, string? username, DateTimeOffset at)
    {
        if (!CodeValid(at))
            throw new DomainException("Код привязки устарел — получите новый");

        (ChatId, Username, LinkedAt) = (chatId, string.IsNullOrWhiteSpace(username) ? null : username.Trim(), at);
        (CodeHash, CodeExpiresAt) = (null, null);
    }

    // Отвязать: бот перестаёт слать уведомления и принимать команды из чата
    public void Unlink() => (ChatId, Username, LinkedAt) = (null, null, null);

    public static string HashOf(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
}
