namespace HhAutoApplier.Application.Commands.Telegram;

// Сообщение боту. Отвечает только в личном чате: в группе ответы видели бы посторонние
public record HandleTelegramMessageCommand
{
    public required long ChatId { get; init; }

    public bool IsPrivate { get; init; }

    public required string Text { get; init; }

    // @username отправителя — запоминается при привязке
    public string? Username { get; init; }
}
