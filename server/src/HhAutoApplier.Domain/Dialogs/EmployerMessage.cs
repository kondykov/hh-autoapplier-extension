namespace HhAutoApplier.Domain.Dialogs;

// Что написал работодатель: отказ — по состоянию отклика hh.ru, без LLM; остальное разбирает LLM
public enum DialogKind
{
    Rejection = 0,
    Invitation = 1,
    Question = 2,
    Other = 3,
}

// Автоответ на отказ (решение владельца 2026-10-26): короткое «спасибо» отправляет расширение, где чат это позволяет
public enum AutoReplyState
{
    // Не нужен: не отказ, старый отказ с первого сбора или пользователь уже ответил сам
    None = 0,
    // Ждёт отправки расширением
    Pending = 1,
    Sent = 2,
    // Написать в чат нельзя (работодатель закрыл переписку) или отправка не удалась
    Unavailable = 3,
}

// Агрегат: последнее сообщение работодателя в чате отклика hh.ru (#26) с разбором и черновиком ответа.
// Одна запись на чат пользователя; новое сообщение работодателя заменяет прежнее и сбрасывает разбор.
// Отвечает работодателю человек; исключение — автоответ на отказ
public class EmployerMessage
{
    // Для EF
    private EmployerMessage()
    {
    }

    public long Id { get; private set; }

    public Guid UserId { get; private set; }

    // Чат hh.ru (chatik)
    public long ChatId { get; private set; }

    // Вакансия чата; null — чат без вакансии
    public string? VacancyId { get; private set; }

    public string Title { get; private set; } = "";

    public string Company { get; private set; } = "";

    public long MessageId { get; private set; }

    public string Text { get; private set; } = "";

    public DateTimeOffset At { get; private set; }

    // Состояние отклика на hh.ru: DISCARD, INVITATION, INTERVIEW, RESPONSE…; null — не сообщено
    public string? HhState { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    // null — ждёт разбора LLM
    public DialogKind? Kind { get; private set; }

    public string? Summary { get; private set; }

    // Черновик ответа от LLM; отправляет человек
    public string? SuggestedReply { get; private set; }

    public string? LlmModel { get; private set; }

    public DateTimeOffset? AnalyzedAt { get; private set; }

    // Пользователь ответил или отметил «обработано»
    public DateTimeOffset? HandledAt { get; private set; }

    public AutoReplyState AutoReply { get; private set; }

    public DateTimeOffset? AutoReplyAt { get; private set; }

    public string? AutoReplyError { get; private set; }

    public bool AwaitsAnalysis => Kind is null;

    public bool IsRejection => HhState == Rejected;

    // Состояние hh.ru: работодатель отказал
    public const string Rejected = "DISCARD";

    // Состояния hh.ru: приглашение или собеседование
    public static bool IsInvitationState(string? state) => state is "INVITATION" or "INTERVIEW";

    // autoReply — отвечать ли на отказ автоматически (false — первый сбор: старые отказы без ответа)
    public static EmployerMessage Create(Guid userId, long chatId, string? vacancyId, string title, string company,
        long messageId, string text, DateTimeOffset at, string? hhState, DateTimeOffset receivedAt, bool autoReply)
    {
        var message = new EmployerMessage { UserId = userId, ChatId = chatId, ReceivedAt = receivedAt };
        message.Receive(vacancyId, title, company, messageId, text, at, hhState, receivedAt, autoReply);
        return message;
    }

    // Сообщение из сбора. Возвращает false, если это уже известное сообщение (меняются только карточка и состояние)
    public bool Receive(string? vacancyId, string title, string company, long messageId, string text, DateTimeOffset at,
        string? hhState, DateTimeOffset receivedAt, bool autoReply)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Пустое сообщение работодателя");

        (VacancyId, Title, Company, HhState) = (vacancyId, Cut(title.Trim(), MaxTitle), Cut(company.Trim(), MaxTitle), hhState);
        if (messageId == MessageId)
            return false;

        // hh.ru отдаёт время с поясом (+03:00), а Npgsql пишет в timestamptz только UTC
        (MessageId, Text, At, ReceivedAt) = (messageId, Cut(text.Trim(), MaxText), at.ToUniversalTime(), receivedAt);
        (Summary, SuggestedReply, LlmModel, AnalyzedAt, HandledAt) = (null, null, null, null, null);
        (AutoReply, AutoReplyAt, AutoReplyError) = (AutoReplyState.None, null, null);

        if (IsRejection)
        {
            // Отказ понятен по состоянию: LLM не нужна, отвечать по существу нечего
            (Kind, AnalyzedAt) = (DialogKind.Rejection, receivedAt);
            if (autoReply)
                AutoReply = AutoReplyState.Pending;
        }
        else
        {
            Kind = null;
        }

        return true;
    }

    public void ApplyAnalysis(DialogKind kind, string summary, string? reply, string model, DateTimeOffset at)
    {
        if (!AwaitsAnalysis)
            throw new DomainException("Сообщение уже разобрано");

        Kind = kind;
        Summary = Cut(summary.Trim(), MaxSummary);
        SuggestedReply = string.IsNullOrWhiteSpace(reply) ? null : Cut(reply.Trim(), MaxText);
        (LlmModel, AnalyzedAt) = (model, at);
    }

    // Последнее сообщение в чате — от пользователя: он ответил сам, автоответ больше не нужен
    public void UserReplied(DateTimeOffset at)
    {
        HandledAt ??= at;
        if (AutoReply == AutoReplyState.Pending)
            AutoReply = AutoReplyState.None;
    }

    public void MarkHandled(DateTimeOffset at) => HandledAt ??= at;

    // Итог автоответа от расширения; error — почему не отправлено
    public void ReportAutoReply(bool sent, string? error, DateTimeOffset at)
    {
        if (AutoReply != AutoReplyState.Pending)
            throw new DomainException($"Автоответ не ждёт отправки, сейчас {AutoReply}");

        AutoReply = sent ? AutoReplyState.Sent : AutoReplyState.Unavailable;
        AutoReplyAt = at;
        AutoReplyError = sent || string.IsNullOrWhiteSpace(error) ? null : Cut(error.Trim(), 500);
        if (sent)
            HandledAt ??= at;
    }

    private static string Cut(string text, int max) => text.Length > max ? text[..max] : text;

    public const int MaxText = 4000;

    public const int MaxTitle = 300;

    public const int MaxSummary = 1000;
}
