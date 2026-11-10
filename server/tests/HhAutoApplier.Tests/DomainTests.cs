using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

public class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly ResumeProfile[] Resumes = [ResumeProfile.Create(".NET", "C# .NET", ["c#"])];

    private static JobApplication New() =>
        JobApplication.Create(Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now), null, Now);

    private static JobApplication Queued()
    {
        var a = New();
        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now);
        return a;
    }

    [Fact]
    public void Application_GoesThroughStatuses()
    {
        var a = Queued();
        Assert.Equal(VacancyStatus.Queued, a.Status);
        Assert.Equal(".NET", a.ResumeProfile);

        a.MarkApplied("PHP", Now);
        Assert.Equal((VacancyStatus.Applied, "PHP", Now), (a.Status, a.ResumeProfile, a.AppliedAt!.Value));

        a.Reject("отказ", Now);
        Assert.Equal((VacancyStatus.Rejected, "отказ"), (a.Status, a.StatusReason));
    }

    [Fact]
    public void Application_RejectsInvalidTransitions()
    {
        var a = Queued();
        Assert.Throws<DomainException>(() => a.Reject("до отклика", Now));
        Assert.Throws<DomainException>(() => a.Requeue(Now));

        a.MarkApplied(null, Now);
        Assert.Throws<DomainException>(() => a.MarkApplied(null, Now));
        Assert.Throws<DomainException>(() => a.Skip("поздно", Now));
        Assert.Throws<DomainException>(() => a.Evaluate(VacancyEvaluator.Default, new FilterRules(), [], Now));
    }

    // Отклик в заданном статусе — настоящими переходами, а не подменой поля
    private static JobApplication InStatus(VacancyStatus status)
    {
        var a = New();
        if (status == VacancyStatus.New)
            return a;

        if (status is VacancyStatus.Pending or VacancyStatus.Review)
        {
            a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now, awaitLlm: true);
            if (status == VacancyStatus.Review)
                a.ApplyLlmEvaluation(LlmVerdict.Doubtful, 55, "не уверена", null, "m", Now, reviewDoubtful: true);
            return a;
        }

        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now);
        if (status == VacancyStatus.Skipped)
            a.Skip("не подходит", Now);
        else if (status == VacancyStatus.Failed)
            a.Fail("капча", Now);
        else if (status == VacancyStatus.Questionnaire)
            a.RequireAnswers([VacancyQuestion.Create("Зарплатные ожидания", QuestionType.Text, [])], Now);
        return a;
    }

    [Theory]
    [InlineData(VacancyStatus.New)]
    [InlineData(VacancyStatus.Pending)]
    [InlineData(VacancyStatus.Review)]
    [InlineData(VacancyStatus.Skipped)]
    [InlineData(VacancyStatus.Failed)]
    public void MarkApplied_NotFromQueueOrQuestionnaire_Rejected(VacancyStatus status)
    {
        var a = InStatus(status);

        Assert.Throws<DomainException>(() => a.MarkApplied("PHP", Now.AddMinutes(1)));

        Assert.Equal(status, a.Status);
        Assert.Null(a.AppliedAt);
    }

    [Theory]
    [InlineData(VacancyStatus.Queued)]
    [InlineData(VacancyStatus.Questionnaire)]
    public void MarkApplied_FromQueueOrQuestionnaire_Applied(VacancyStatus status)
    {
        var a = InStatus(status);

        a.MarkApplied("PHP", Now.AddMinutes(1));

        Assert.Equal((VacancyStatus.Applied, "PHP", Now.AddMinutes(1)), (a.Status, a.ResumeProfile, a.AppliedAt!.Value));
    }

    // Чат на hh.ru есть — отклик у работодателя, в каком бы статусе он ни остался на сервере
    [Theory]
    [InlineData(VacancyStatus.New)]
    [InlineData(VacancyStatus.Pending)]
    [InlineData(VacancyStatus.Review)]
    [InlineData(VacancyStatus.Queued)]
    [InlineData(VacancyStatus.Questionnaire)]
    [InlineData(VacancyStatus.Skipped)]
    [InlineData(VacancyStatus.Failed)]
    public void RecoverApplied_FromAnyStatusBeforeSending(VacancyStatus status)
    {
        var a = InStatus(status);

        a.RecoverApplied(Now.AddMinutes(1));

        Assert.Equal((VacancyStatus.Applied, Now.AddMinutes(1)), (a.Status, a.AppliedAt!.Value));
        Assert.Equal((status, VacancyStatus.Applied), (a.History[^1].From, a.History[^1].To));
    }

    [Fact]
    public void RecoverApplied_AlreadySent_Rejected()
    {
        var a = Queued();
        a.MarkApplied(null, Now);

        Assert.Throws<DomainException>(() => a.RecoverApplied(Now.AddMinutes(1)));
        Assert.Equal(Now, a.AppliedAt);
    }

    [Fact]
    public void Application_SkippedOrFailed_CanBeRequeued()
    {
        var a = Queued();
        a.Fail("капча", Now);
        a.Requeue(Now);
        Assert.Equal((VacancyStatus.Queued, null), (a.Status, a.StatusReason));
    }

    [Fact]
    public void Application_HistoryStartsWithNew_AndRecordsTransitionsInOrder()
    {
        var a = New();
        Assert.Equal(("1", VacancyStatus.New), (a.VacancyId, a.Status));
        var created = Assert.Single(a.History);
        Assert.Equal((Now, null as VacancyStatus?, VacancyStatus.New, null as string), (created.At, created.From, created.To, created.Reason));

        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now.AddMinutes(1));
        a.Fail("капча", Now.AddMinutes(2));
        a.Requeue(Now.AddMinutes(3));
        a.MarkApplied(null, Now.AddMinutes(4));
        a.Reject("отказ", Now.AddMinutes(5));

        (DateTimeOffset, VacancyStatus?, VacancyStatus, string?)[] expected =
        [
            (Now, null, VacancyStatus.New, null),
            (Now.AddMinutes(1), VacancyStatus.New, VacancyStatus.Queued, null),
            (Now.AddMinutes(2), VacancyStatus.Queued, VacancyStatus.Failed, "капча"),
            (Now.AddMinutes(3), VacancyStatus.Failed, VacancyStatus.Queued, null),
            (Now.AddMinutes(4), VacancyStatus.Queued, VacancyStatus.Applied, null),
            (Now.AddMinutes(5), VacancyStatus.Applied, VacancyStatus.Rejected, "отказ"),
        ];
        Assert.Equal(expected, a.History.Select(e => (e.At, e.From, e.To, e.Reason)).ToArray());
    }

    [Fact]
    public void Application_SameStatus_ChangesOnlyReason()
    {
        var a = Queued();
        a.Skip("не подходит", Now.AddMinutes(1));
        var count = a.History.Count;

        // Повторный пропуск — новая причина, но не новый переход
        a.Skip("совсем не подходит", Now.AddMinutes(2));
        Assert.Equal((VacancyStatus.Skipped, "совсем не подходит"), (a.Status, a.StatusReason));
        Assert.Equal(count, a.History.Count);
        Assert.Equal("не подходит", a.History[^1].Reason);
    }

    [Fact]
    public void Application_EvaluateAwaitingLlm_RecordsPending()
    {
        var a = New();
        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now.AddMinutes(1), awaitLlm: true);
        Assert.Equal(VacancyStatus.Pending, a.Status);

        var pending = a.History[^1];
        Assert.Equal((Now.AddMinutes(1), VacancyStatus.New, VacancyStatus.Pending), (pending.At, pending.From, pending.To));

        a.ApplyLlmEvaluation(LlmVerdict.Doubtful, 55, "не уверена", null, "m", Now.AddMinutes(2), reviewDoubtful: true);
        var review = a.History[^1];
        Assert.Equal((VacancyStatus.Pending, VacancyStatus.Review, "LLM сомневается: не уверена"), (review.From, review.To, review.Reason));
        Assert.Equal(3, a.History.Count);
    }

    [Fact]
    public void Salary_FromColumnsAndToRub()
    {
        Assert.Null(Salary.From(null, null, "USD"));
        Assert.Equal(new Salary(100, null, "RUR"), Salary.From(100, null, null));
        Assert.Equal(270_000, Salary.From(null, 3000, "usd")!.ToRub());
        Assert.Null(Salary.From(1, null, "XXX")!.ToRub());
        Assert.Equal("от 100 до 200 RUR", new Salary(100, 200, "RUR").ToString());
    }

    [Fact]
    public void Profiles_ValidateRequiredFields()
    {
        Assert.Throws<DomainException>(() => SearchProfile.Create(" ", "C#", null, null, null, true));
        Assert.Throws<DomainException>(() => ResumeProfile.Create(".NET", "", ["c#"]));

        var search = SearchProfile.Create("dotnet", " C# ", "", "&work_format=REMOTE", null, true);
        Assert.Equal(("C#", null, "work_format=REMOTE"), (search.Query, search.Area, search.ExtraParams));
        Assert.Equal(["c#", ".net"], ResumeProfile.Create(".NET", "C#", ["c#", " .net ", "C#", ""]).Keywords);
    }

    // Ссылка из импорта открывается на страницах backend: только https на hh.ru и его поддоменах, иначе — стандартная
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://hh.ru/vacancy/1")]
    [InlineData("https://evil.com/vacancy/1")]
    [InlineData("https://evilhh.ru/vacancy/1")]
    [InlineData("https://hh.ru.evil.com/vacancy/1")]
    [InlineData("/vacancy/1")]
    [InlineData("")]
    [InlineData(null)]
    public void Vacancy_UnsafeUrl_ReplacedWithDefault(string? url)
    {
        var vacancy = Vacancy.Create("1", "C# разработчик", url!, "dotnet", Now, imported: true);

        Assert.Equal("https://hh.ru/vacancy/1", vacancy.Url);
        Assert.True(vacancy.Imported);
    }

    [Theory]
    [InlineData("https://hh.ru/vacancy/1")]
    [InlineData("https://spb.hh.ru/vacancy/1")]
    public void Vacancy_HhUrl_Kept(string url) =>
        Assert.Equal(url, Vacancy.Create("1", "C# разработчик", url, "dotnet", Now).Url);

    [Fact]
    public void MarkVacancyUnavailable_OnlyFromNew()
    {
        var waiting = New();

        waiting.MarkVacancyUnavailable(Now);

        Assert.Equal((VacancyStatus.Skipped, "вакансия недоступна"), (waiting.Status, waiting.StatusReason));
        Assert.Throws<DomainException>(() => waiting.MarkVacancyUnavailable(Now));

        var queued = Queued();
        Assert.Throws<DomainException>(() => queued.MarkVacancyUnavailable(Now));
        Assert.Equal(VacancyStatus.Queued, queued.Status);
    }

    // Отправленный отклик: письмо уже у работодателя (#70)
    private static JobApplication Sent(VacancyStatus status)
    {
        var a = Queued();
        a.MarkApplied(null, Now);
        if (status == VacancyStatus.Invited)
            a.Invite(null, Now);
        else if (status == VacancyStatus.Rejected)
            a.Reject(null, Now);
        return a;
    }

    [Theory]
    [InlineData(VacancyStatus.New)]
    [InlineData(VacancyStatus.Pending)]
    [InlineData(VacancyStatus.Review)]
    [InlineData(VacancyStatus.Questionnaire)]
    [InlineData(VacancyStatus.Queued)]
    public void CloseVacancyArchived_FromEveryWaitingStatus_WithReasonAndHistory(VacancyStatus status)
    {
        var a = InStatus(status);

        a.CloseVacancyArchived(Now);

        Assert.Equal((VacancyStatus.Skipped, "вакансия в архиве"), (a.Status, a.StatusReason));
        Assert.True(a.ClosedAsArchived);
        var last = a.History[^1];
        Assert.Equal((status, VacancyStatus.Skipped, "вакансия в архиве"), (last.From!.Value, last.To, last.Reason));
    }

    [Theory]
    [InlineData(VacancyStatus.Pending)]
    [InlineData(VacancyStatus.Review)]
    [InlineData(VacancyStatus.Questionnaire)]
    [InlineData(VacancyStatus.Queued)]
    public void CloseVacancyUnavailable_FromEveryWaitingStatus(VacancyStatus status)
    {
        var a = InStatus(status);

        a.CloseVacancyUnavailable(Now);

        Assert.Equal((VacancyStatus.Skipped, "вакансия недоступна"), (a.Status, a.StatusReason));
        Assert.False(a.ClosedAsArchived);
    }

    [Theory]
    [InlineData(VacancyStatus.Applied)]
    [InlineData(VacancyStatus.Invited)]
    [InlineData(VacancyStatus.Rejected)]
    public void CloseVacancy_SentApplications_NotTouched(VacancyStatus status)
    {
        var a = Sent(status);
        var reason = a.StatusReason;

        Assert.Throws<DomainException>(() => a.CloseVacancyArchived(Now));
        Assert.Throws<DomainException>(() => a.CloseVacancyUnavailable(Now));

        Assert.Equal((status, reason), (a.Status, a.StatusReason));
    }

    // Уже решённый отклик остаётся со своей причиной
    [Theory]
    [InlineData(VacancyStatus.Skipped)]
    [InlineData(VacancyStatus.Failed)]
    public void CloseVacancy_DecidedApplications_NotTouched(VacancyStatus status)
    {
        var a = InStatus(status);
        var reason = a.StatusReason;

        Assert.Throws<DomainException>(() => a.CloseVacancyArchived(Now));

        Assert.Equal((status, reason), (a.Status, a.StatusReason));
    }

    [Fact]
    public void ArchivedApplication_CanBeRequeued()
    {
        var a = Queued();
        a.CloseVacancyArchived(Now);

        a.Requeue(Now);

        Assert.Equal((VacancyStatus.Queued, false), (a.Status, a.ClosedAsArchived));
    }

    // Причина от расширения приходит обычным статусом: признак тот же
    [Fact]
    public void ArchivedReasonFromExtension_SetsFlag()
    {
        var a = Queued();

        a.Skip("вакансия в архиве", Now);

        Assert.True(a.ClosedAsArchived);
    }

    [Fact]
    public void Vacancy_LastSeen_StartsAtCollected_AndOnlyMovesForward()
    {
        var v = Vacancy.Create("1", "x", "", "dotnet", Now);
        Assert.Equal(Now, v.LastSeenAt);
        Assert.Null(v.ArchiveCheckedAt);

        v.MarkSeen(Now.AddHours(2));
        v.MarkSeen(Now.AddHours(1));

        Assert.Equal(Now.AddHours(2), v.LastSeenAt);
        Assert.Equal(Now, v.CollectedAt);
    }

    [Fact]
    public void Vacancy_AcceptSourceCard_KeepsLastSeen()
    {
        var v = Vacancy.Create("1", "x", "", "dotnet", Now, imported: true);
        v.MarkSeen(Now.AddHours(3));

        v.AcceptSourceCard(Vacancy.Create("1", "y", "", "dotnet", Now.AddHours(9)));

        Assert.Equal(Now.AddHours(3), v.LastSeenAt);
    }

    [Fact]
    public void Vacancy_AcceptSourceCard_ReplacesClientCard()
    {
        var imported = Vacancy.Create("1", "Чужой заголовок", "javascript:alert(1)", "dotnet", Now, imported: true);
        imported.UpdateCard("Фейк", "Нигде", new Salary(1, 2, "RUR"), "OFFICE", true, Now);
        var card = Vacancy.Create("1", "C# разработчик", "https://spb.hh.ru/vacancy/1", "dotnet", Now);
        card.UpdateCard("Acme", "Москва", null, "REMOTE", false, Now.AddDays(-1));

        imported.AcceptSourceCard(card);

        Assert.Equal(("C# разработчик", "https://spb.hh.ru/vacancy/1", "Acme", "Москва", "REMOTE", false, Now.AddDays(-1)),
            (imported.Title, imported.Url, imported.Company, imported.Area, imported.WorkFormat, imported.HasTest, imported.PublishedAt));
        Assert.Null(imported.Salary);
        Assert.False(imported.Imported);
        Assert.Throws<DomainException>(() => imported.AcceptSourceCard(Vacancy.Create("2", "x", "", "dotnet", Now)));
    }
}
