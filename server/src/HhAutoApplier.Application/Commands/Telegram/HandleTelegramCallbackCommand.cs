namespace HhAutoApplier.Application.Commands.Telegram;

// Нажатие кнопки под сообщением бота: Data — «approve:<id вакансии>» или «decline:<id вакансии>»
public record HandleTelegramCallbackCommand
{
    public required long ChatId { get; init; }

    public required string Data { get; init; }
}
