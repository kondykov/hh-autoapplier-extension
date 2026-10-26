namespace HhAutoApplier.Application.Dtos;

// Состояние привязки Telegram у текущего пользователя
public record TelegramChatDto(bool Configured, bool Linked, string? Username, DateTimeOffset? LinkedAt);

// Код привязки показывается один раз; Link — ссылка на бота с кодом (null — имя бота не получено, код отправить вручную: /start <код>)
public record TelegramLinkDto(string Code, string? Link, DateTimeOffset ExpiresAt);

// Ответ бота: текст и кнопки под ним (Data возвращается боту при нажатии)
public record BotMessage(string Text, IReadOnlyList<BotButton> Buttons)
{
    public BotMessage(string text) : this(text, [])
    {
    }
}

public record BotButton(string Text, string Data);

// Нажатие кнопки: Notice — всплывающая подсказка; Status — дописать к сообщению и убрать кнопки (null — сообщение не трогать)
public record BotCallbackResult(string Notice, string? Status);

public record ReportEventResultDto(bool Notified);
