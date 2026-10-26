namespace HhAutoApplier.Domain.Notifications;

public enum NotificationKind
{
    // Сомнительный отклик ждёт ручной проверки: в сообщении кнопки «Одобрить» и «Отклонить»
    Review = 0,
    // Расширение встретило капчу и остановилось
    Captcha = 1,
    // Расширение исчерпало дневной лимит откликов
    DailyLimit = 2,
    // LLM не ответила на анкету: отклик пропущен
    Questionnaire = 3,
    // Администратору: источник вакансий заблокировал сбор (антибот)
    SourceBlocked = 4,
    // Администратору: LLM недоступна или исчерпан дневной лимит запросов
    LlmUnavailable = 5,
}

// Агрегат: уведомление пользователю в Telegram (outbox). Сценарий записывает его вместе со своими данными,
// фоновая отправка доставляет. Key — защита от повторов: одно событие (отклик на проверке, капча за час) — одно сообщение
public class Notification
{
    // Для EF
    private Notification()
    {
    }

    public long Id { get; private set; }

    public Guid UserId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public string Key { get; private set; } = "";

    public string Text { get; private set; } = "";

    // Отклик на проверке: кнопки бота действуют на эту вакансию
    public string? VacancyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public int Attempts { get; private set; }

    public string? Error { get; private set; }

    public bool Pending => SentAt is null && Attempts < MaxAttempts;

    public static Notification Create(Guid userId, NotificationKind kind, string key, string text, DateTimeOffset at, string? vacancyId = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new DomainException("У уведомления нет ключа");

        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Пустое уведомление");

        text = text.Trim();
        return new Notification
        {
            UserId = userId,
            Kind = kind,
            Key = key,
            // Лимит сообщения Telegram — 4096 символов
            Text = text.Length > MaxTextLength ? text[..(MaxTextLength - 1)] + "…" : text,
            VacancyId = vacancyId,
            CreatedAt = at,
        };
    }

    public void MarkSent(DateTimeOffset at) => (SentAt, Error) = (at, null);

    // Неудачная попытка; после MaxAttempts уведомление больше не отправляется
    public void MarkFailed(string error)
    {
        Attempts++;
        Error = error.Length > 500 ? error[..500] : error;
    }

    // Доставить некуда (чат отвязан, бот заблокирован пользователем): больше не пытаться
    public void Cancel(string reason)
    {
        Attempts = MaxAttempts;
        Error = reason.Length > 500 ? reason[..500] : reason;
    }

    public const int MaxAttempts = 5;

    public const int MaxTextLength = 4000;
}
