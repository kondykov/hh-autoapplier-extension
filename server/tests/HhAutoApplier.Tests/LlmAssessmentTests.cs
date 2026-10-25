using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

public class LlmAssessmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly ResumeProfile[] Resumes =
    [
        ResumeProfile.Create(".NET", "C# .NET", ["c#"], "5 лет на C#, ASP.NET Core, PostgreSQL"),
        ResumeProfile.Create("PHP", "PHP Laravel", ["php"]),
    ];

    private static JobApplication Pending()
    {
        var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateDetails(null, "Пишем на ASP.NET Core", []);
        var a = JobApplication.Create(v, null, Now);
        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, Now, awaitLlm: true);
        return a;
    }

    [Fact]
    public void Parse_JsonInsideText_AndResumeByName()
    {
        var a = VacancyAssessment.Parse("""Ответ: {"verdict": "Doubtful", "score": "55", "resume": "php", "reason": "мало опыта"} конец""", Resumes);
        Assert.Equal(new LlmAssessment(LlmVerdict.Doubtful, 55, "PHP", "мало опыта"), a);
    }

    [Fact]
    public void Parse_FallbacksAndGarbage()
    {
        var a = VacancyAssessment.Parse("""{"verdict": "fit", "resume": "Java"}""", Resumes);
        Assert.Equal((LlmVerdict.Fit, null as string), (a!.Verdict, a.Resume));
        Assert.InRange(a.Score, 1, 100);

        Assert.Null(VacancyAssessment.Parse("не знаю", Resumes));
        Assert.Null(VacancyAssessment.Parse("""{"verdict": "maybe"}""", Resumes));
        Assert.Null(VacancyAssessment.Parse("{verdict: fit", Resumes));
    }

    [Fact]
    public void BuildRequest_HasResumesRulesAndVacancy()
    {
        var request = VacancyAssessment.BuildRequest(Pending().Vacancy, Resumes, new FilterRules { MinSalaryRub = 200_000, ExcludeKeywords = ["1С"] });
        Assert.True(request.Json);
        var text = VacancyAssessment.Serialize(request);
        Assert.Contains("5 лет на C#", text);
        Assert.Contains("200000", text);
        Assert.Contains("1С", text);
        Assert.Contains("Пишем на ASP.NET Core", text);
    }

    [Fact]
    public void Application_PendingUntilLlm_ThenQueuedReviewOrSkipped()
    {
        var fit = Pending();
        Assert.Equal(VacancyStatus.Pending, fit.Status);
        fit.ApplyLlmEvaluation(LlmVerdict.Fit, 140, "стек совпадает", "PHP", "m", Now, reviewDoubtful: true);
        Assert.Equal((VacancyStatus.Queued, 100, "PHP", null as string), (fit.Status, fit.Score, fit.ResumeProfile, fit.StatusReason));
        Assert.Throws<DomainException>(() => fit.ApplyLlmEvaluation(LlmVerdict.Fit, 90, "", null, "m", Now, reviewDoubtful: true));

        // Сомнительная — на ручную проверку; правило поиска «без проверки» — сразу в очередь
        var doubtful = Pending();
        doubtful.ApplyLlmEvaluation(LlmVerdict.Doubtful, 55, "не уверена", null, "m", Now, reviewDoubtful: true);
        Assert.Equal((VacancyStatus.Review, "LLM сомневается: не уверена"), (doubtful.Status, doubtful.StatusReason));
        var noReview = Pending();
        noReview.ApplyLlmEvaluation(LlmVerdict.Doubtful, 55, "не уверена", null, "m", Now, reviewDoubtful: false);
        Assert.Equal(VacancyStatus.Queued, noReview.Status);

        var reject = Pending();
        reject.ApplyLlmEvaluation(LlmVerdict.Reject, 10, "1С", null, "m", Now, reviewDoubtful: true);
        Assert.Equal((VacancyStatus.Skipped, "LLM: 1С", "m"), (reject.Status, reject.StatusReason, reject.LlmModel));
    }

    [Fact]
    public void Application_ReviewApproveWithLetterOrDecline()
    {
        JobApplication Review()
        {
            var v = Pending();
            v.ApplyLlmEvaluation(LlmVerdict.Doubtful, 55, "не уверена", null, "m", Now, reviewDoubtful: true);
            v.SetCoverLetter("Письмо LLM", "m", Now);
            return v;
        }

        var kept = Review();
        kept.Approve(null, Now);
        Assert.Equal((VacancyStatus.Queued, "Письмо LLM", "одобрено вручную"), (kept.Status, kept.CoverLetter, kept.StatusReason));
        Assert.Throws<DomainException>(() => kept.Approve(null, Now));

        var edited = Review();
        edited.Approve("  Моё письмо  ", Now);
        Assert.Equal(("Моё письмо", JobApplication.ManualLetter), (edited.CoverLetter, edited.CoverLetterModel));

        var declined = Review();
        declined.Decline(" не мой стек ", Now);
        Assert.Equal((VacancyStatus.Skipped, "отклонено вручную: не мой стек"), (declined.Status, declined.StatusReason));
        Assert.Throws<DomainException>(() => declined.Decline(null, Now));
    }
}
