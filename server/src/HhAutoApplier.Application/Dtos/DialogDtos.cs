using HhAutoApplier.Domain.Dialogs;

namespace HhAutoApplier.Application.Dtos;

// Чат, где ждёт автоответ на отказ: расширение отправляет свой шаблон и сообщает итог
public record AutoReplyDto(long ChatId, string? VacancyId, string Title, string Company);

// Итог приёма чатов: сколько новых сообщений работодателей, сколько откликов стали отказами и приглашениями,
// сколько потерянных откликов найдено по чатам (Recovered → Applied)
public record ImportDialogsResultDto(int Received, int NewMessages, int Rejected, int Invited, int Recovered, IReadOnlyList<AutoReplyDto> AutoReplies);

public record DialogsAnalysisResultDto(int Analyzed, int Failed, int Remaining, string? StoppedBy);

public record EmployerMessageDto
{
    public long ChatId { get; init; }

    public string? VacancyId { get; init; }

    public string Title { get; init; } = "";

    public string Company { get; init; } = "";

    public string Text { get; init; } = "";

    public DateTimeOffset At { get; init; }

    public string? HhState { get; init; }

    public DialogKind? Kind { get; init; }

    public string? Summary { get; init; }

    public string? SuggestedReply { get; init; }

    public string? LlmModel { get; init; }

    public DateTimeOffset? HandledAt { get; init; }

    public AutoReplyState AutoReply { get; init; }

    public string? AutoReplyError { get; init; }
}
