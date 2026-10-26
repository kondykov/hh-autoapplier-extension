using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Dialogs;

namespace HhAutoApplier.Tests;

// Ответы работодателей (#26): запись чата, отказ без LLM, автоответ, разбор ответа модели
public class DialogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static EmployerMessage Message(string? state, long messageId = 1, bool autoReply = true) =>
        EmployerMessage.Create(Guid.NewGuid(), 10, "100", " C# разработчик ", "Acme", messageId, " Здравствуйте! ", Now, state, Now, autoReply);

    [Fact]
    public void Rejection_KnownByState_WithoutLlm_AutoReplyPending()
    {
        var message = Message("DISCARD");

        Assert.Equal(DialogKind.Rejection, message.Kind);
        Assert.False(message.AwaitsAnalysis);
        Assert.Equal(AutoReplyState.Pending, message.AutoReply);
        Assert.Equal(("C# разработчик", "Здравствуйте!"), (message.Title, message.Text));

        // Старый отказ с первого сбора — без автоответа
        Assert.Equal(AutoReplyState.None, Message("DISCARD", autoReply: false).AutoReply);
    }

    [Fact]
    public void Invitation_AwaitsLlm_NoAutoReply()
    {
        var message = Message("INTERVIEW");

        Assert.True(message.AwaitsAnalysis);
        Assert.Equal(AutoReplyState.None, message.AutoReply);

        message.ApplyAnalysis(DialogKind.Invitation, " Зовут на звонок ", " Спасибо! Удобно [время]. ", "m", Now);
        Assert.Equal((DialogKind.Invitation, "Зовут на звонок", "Спасибо! Удобно [время]."), (message.Kind!.Value, message.Summary, message.SuggestedReply));
        Assert.Throws<DomainException>(() => message.ApplyAnalysis(DialogKind.Other, "", null, "m", Now));
    }

    [Fact]
    public void NewEmployerMessage_ResetsAnalysisAndHandled_SameMessageDoesNot()
    {
        var message = Message("RESPONSE");
        message.ApplyAnalysis(DialogKind.Question, "спрашивают зарплату", "[ожидания]", "m", Now);
        message.MarkHandled(Now);

        Assert.False(message.Receive("100", "C#", "Acme", 1, "то же", Now, "RESPONSE", Now, true));
        Assert.Equal(DialogKind.Question, message.Kind);

        Assert.True(message.Receive("100", "C#", "Acme", 2, "Когда сможете выйти?", Now.AddHours(1), "RESPONSE", Now.AddHours(1), true));
        Assert.True(message.AwaitsAnalysis);
        Assert.Null(message.HandledAt);
        Assert.Null(message.SuggestedReply);
    }

    [Fact]
    public void AutoReply_ReportedOnce_UserReplyCancelsIt()
    {
        var sent = Message("DISCARD");
        sent.ReportAutoReply(true, null, Now);
        Assert.Equal(AutoReplyState.Sent, sent.AutoReply);
        Assert.NotNull(sent.HandledAt);
        Assert.Throws<DomainException>(() => sent.ReportAutoReply(true, null, Now));

        var closed = Message("DISCARD");
        closed.ReportAutoReply(false, "переписка закрыта", Now);
        Assert.Equal((AutoReplyState.Unavailable, "переписка закрыта"), (closed.AutoReply, closed.AutoReplyError));
        Assert.Null(closed.HandledAt);

        var manual = Message("DISCARD");
        manual.UserReplied(Now);
        Assert.Equal(AutoReplyState.None, manual.AutoReply);
        Assert.NotNull(manual.HandledAt);
    }

    [Fact]
    public void EmptyText_Rejected()
    {
        Assert.Throws<DomainException>(() =>
            EmployerMessage.Create(Guid.NewGuid(), 1, null, "", "", 1, "  ", Now, null, Now, true));
    }

    [Theory]
    [InlineData("""{"kind": "invitation", "summary": "звонок завтра", "reply": "Спасибо, удобно [время]."}""", DialogKind.Invitation, "Спасибо, удобно [время].")]
    [InlineData("""рассуждения {"kind": "Question", "summary": "зарплата", "reply": "Ожидаю [сумма]."} хвост""", DialogKind.Question, "Ожидаю [сумма].")]
    [InlineData("""{"kind": "other", "summary": "резюме просмотрено", "reply": null}""", DialogKind.Other, null)]
    [InlineData("""{"kind": "rejection", "summary": "отказ", "reply": "null"}""", DialogKind.Rejection, null)]
    public void Analysis_Parsed(string content, DialogKind kind, string? reply)
    {
        var analysis = DialogAnalysis.Parse(content);

        Assert.NotNull(analysis);
        Assert.Equal((kind, reply), (analysis.Kind, analysis.Reply));
    }

    [Theory]
    [InlineData("не JSON")]
    [InlineData("""{"kind": "maybe", "summary": "?"}""")]
    [InlineData("""{"summary": "без вида"}""")]
    public void Analysis_NotParsed(string content) => Assert.Null(DialogAnalysis.Parse(content));

    [Fact]
    public void Prompt_HasVacancyStateAndMessage()
    {
        var message = Message("INTERVIEW");
        var prompt = VacancyAssessment.Serialize(DialogAnalysis.BuildRequest(message, null));

        Assert.Contains("C# разработчик", prompt);
        Assert.Contains("INTERVIEW", prompt);
        Assert.Contains("Здравствуйте!", prompt);
        Assert.True(DialogAnalysis.BuildRequest(message, null).Json);
    }
}
