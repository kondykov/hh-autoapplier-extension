using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

// Оценить заново и скрыть (#71)
public class ReevaluateHideTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static readonly ResumeProfile[] Resumes = [ResumeProfile.Create(".NET", "C# .NET", ["c#"])];

    private static JobApplication New(bool imported = false) =>
        JobApplication.Create(Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now, imported), null, Now);

    private static JobApplication Pending()
    {
        var a = New();
        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now, awaitLlm: true);
        return a;
    }

    private static JobApplication Queued()
    {
        var a = New();
        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now);
        return a;
    }

    private static JobApplication Review()
    {
        var a = Pending();
        a.ApplyLlmEvaluation(LlmVerdict.Doubtful, 50, "сомнения", null, "m", Now, reviewDoubtful: true);
        return a;
    }

    private static JobApplication Questionnaire()
    {
        var a = Queued();
        a.RequireAnswers([VacancyQuestion.Create("Опыт?", QuestionType.Text, null)], Now);
        return a;
    }

    private static JobApplication Skipped()
    {
        var a = Queued();
        a.Skip("пропущено вручную", Now);
        return a;
    }

    private static JobApplication Failed()
    {
        var a = Queued();
        a.Fail("ошибка", Now);
        return a;
    }

    private static JobApplication Applied()
    {
        var a = Queued();
        a.MarkApplied(null, Now);
        return a;
    }

    private static JobApplication Invited()
    {
        var a = Applied();
        a.Invite("приглашение", Now);
        return a;
    }

    private static JobApplication Rejected()
    {
        var a = Applied();
        a.Reject("отказ", Now);
        return a;
    }

    private static void Reevaluate(JobApplication a, FilterRules? rules = null) =>
        a.Reevaluate(VacancyEvaluator.Default, rules ?? new FilterRules(), Resumes, Later);

    public static TheoryData<string> NotDismissed => ["New", "Pending", "Review", "Questionnaire", "Queued", "Failed", "Applied", "Invited", "Rejected"];

    private static JobApplication InStatus(string status) => status switch
    {
        "New" => New(),
        "Pending" => Pending(),
        "Review" => Review(),
        "Questionnaire" => Questionnaire(),
        "Queued" => Queued(),
        "Failed" => Failed(),
        "Applied" => Applied(),
        "Invited" => Invited(),
        "Rejected" => Rejected(),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    [Fact]
    public void Reevaluate_ManualDecline_GoesToPending_WithHistory()
    {
        var a = Review();
        a.Decline(null, Now);
        Assert.True(a.Dismissed);

        Reevaluate(a);

        Assert.Equal((VacancyStatus.Pending, null), (a.Status, a.StatusReason));
        Assert.Contains(a.History, e => e is { From: VacancyStatus.Skipped, To: VacancyStatus.New, Reason: JobApplication.Reevaluation } && e.At == Later);
        Assert.Equal(VacancyStatus.Pending, a.History[^1].To);
    }

    [Fact]
    public void Reevaluate_LlmReject_GoesToPending_AndDropsOldVerdict()
    {
        var a = Pending();
        a.ApplyLlmEvaluation(LlmVerdict.Reject, 10, "не подходит", null, "m", Now, reviewDoubtful: true);
        Assert.True(a.Dismissed);

        Reevaluate(a);

        Assert.Equal(VacancyStatus.Pending, a.Status);
        Assert.Equal((null, null, null, null, null), (a.LlmVerdict, a.LlmScore, a.LlmReason, a.LlmModel, a.LlmEvaluatedAt));
    }

    [Fact]
    public void Reevaluate_RulesSkip_RunsRulesAgain()
    {
        var a = New();
        var strict = new FilterRules { ExcludeKeywords = ["C#"] };
        a.Evaluate(VacancyEvaluator.Default, strict, Resumes, Now);
        Assert.Equal(VacancyStatus.Skipped, a.Status);

        // Правила поправили — отклик проходит
        Reevaluate(a);

        Assert.Equal(VacancyStatus.Pending, a.Status);
    }

    [Fact]
    public void Reevaluate_RulesStillSkip_StaysSkipped_WithNewReason_AndHistory()
    {
        var a = Skipped();

        Reevaluate(a, new FilterRules { ExcludeKeywords = ["C#"] });

        Assert.Equal((VacancyStatus.Skipped, "стоп-слово «C#»"), (a.Status, a.StatusReason));
        Assert.Contains(a.History, e => e.Reason == JobApplication.Reevaluation);
    }

    [Fact]
    public void Reevaluate_Imported_GoesToNew_WithoutRules()
    {
        var a = New(imported: true);
        a.MarkVacancyUnavailable(Now);

        // Правила отсеяли бы карточку, но обрывку с выдачи верить нельзя: сначала подтверждение
        Reevaluate(a, new FilterRules { ExcludeKeywords = ["C#"] });

        Assert.Equal((VacancyStatus.New, JobApplication.Reevaluation), (a.Status, a.StatusReason));
    }

    [Theory]
    [MemberData(nameof(NotDismissed))]
    public void Reevaluate_NotDismissed_Throws_AndChangesNothing(string status)
    {
        var a = InStatus(status);
        var history = a.History.Count;

        Assert.False(a.Dismissed);
        Assert.Throws<DomainException>(() => Reevaluate(a));

        Assert.Equal((status, history), (a.Status.ToString(), a.History.Count));
    }

    [Fact]
    public void Reevaluate_ResetsLlmLetter()
    {
        var a = Queued();
        a.SetCoverLetter("Письмо LLM", "model", Now);
        a.Skip("пропущено вручную", Now);

        Reevaluate(a);

        Assert.Equal((null, null, null), (a.CoverLetter, a.CoverLetterModel, a.CoverLetterAt));
    }

    [Fact]
    public void Reevaluate_KeepsManualLetter()
    {
        var a = Review();
        a.Approve("Письмо человека", Now);
        a.Skip("пропущено вручную", Now);

        Reevaluate(a);

        Assert.Equal(("Письмо человека", JobApplication.ManualLetter), (a.CoverLetter, a.CoverLetterModel));
    }

    [Fact]
    public void Reevaluate_ResetsQuestionnaire()
    {
        var a = Questionnaire();
        a.AnswerQuestions([["Пять лет"]], Now, reviewText: false);
        a.Skip("пропущено вручную", Now);

        Reevaluate(a);

        Assert.Empty(a.Questions);
    }

    [Fact]
    public void Requeue_KeepsLetterAndQuestionnaire()
    {
        var a = Questionnaire();
        a.AnswerQuestions([["Пять лет"]], Now, reviewText: false);
        a.SetCoverLetter("Письмо LLM", "model", Now);
        a.Skip("пропущено вручную", Now);

        a.Requeue(Later);

        Assert.Equal("Письмо LLM", a.CoverLetter);
        Assert.Single(a.Questions);
    }

    [Fact]
    public void Hide_ChangesOnlyDisplay()
    {
        var a = Skipped();
        var (status, reason, history) = (a.Status, a.StatusReason, a.History.Count);

        a.Hide(Later);

        Assert.Equal(Later, a.HiddenAt);
        Assert.Equal((status, reason, history), (a.Status, a.StatusReason, a.History.Count));
    }

    [Fact]
    public void Hide_Twice_KeepsFirstDate()
    {
        var a = Skipped();
        a.Hide(Now);

        a.Hide(Later);

        Assert.Equal(Now, a.HiddenAt);
    }

    [Theory]
    [MemberData(nameof(NotDismissed))]
    public void Hide_NotDismissed_Throws(string status)
    {
        var a = InStatus(status);

        Assert.Throws<DomainException>(() => a.Hide(Later));

        Assert.Null(a.HiddenAt);
    }

    [Fact]
    public void Unhide_ReturnsToList_StatusUnchanged()
    {
        var a = Skipped();
        a.Hide(Now);

        a.Unhide();

        Assert.Equal((null, VacancyStatus.Skipped), (a.HiddenAt, a.Status));
    }

    [Fact]
    public void Reevaluate_ClearsHidden()
    {
        var a = Skipped();
        a.Hide(Now);

        Reevaluate(a);

        Assert.Null(a.HiddenAt);
    }

    [Fact]
    public void Reevaluate_RulesStillSkip_ClearsHidden()
    {
        var a = Skipped();
        a.Hide(Now);

        Reevaluate(a, new FilterRules { ExcludeKeywords = ["C#"] });

        Assert.Equal((VacancyStatus.Skipped, null), (a.Status, a.HiddenAt));
    }

    [Fact]
    public void Requeue_ClearsHidden()
    {
        var a = Skipped();
        a.Hide(Now);

        a.Requeue(Later);

        Assert.Equal((VacancyStatus.Queued, null), (a.Status, a.HiddenAt));
    }

    [Fact]
    public void RecoverApplied_ClearsHidden()
    {
        var a = Skipped();
        a.Hide(Now);

        a.RecoverApplied(Later);

        Assert.Equal((VacancyStatus.Applied, null), (a.Status, a.HiddenAt));
    }
}
