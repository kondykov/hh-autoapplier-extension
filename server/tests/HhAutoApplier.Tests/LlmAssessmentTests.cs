using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
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

    private static Vacancy Pending()
    {
        var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateDetails(null, "Пишем на ASP.NET Core", []);
        v.Evaluate(VacancyEvaluator.Default, new FilterRules(), Resumes, awaitLlm: true);
        return v;
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
        var request = VacancyAssessment.BuildRequest(Pending(), Resumes, new FilterRules { MinSalaryRub = 200_000, ExcludeKeywords = ["1С"] });
        Assert.True(request.Json);
        var text = VacancyAssessment.Serialize(request);
        Assert.Contains("5 лет на C#", text);
        Assert.Contains("200000", text);
        Assert.Contains("1С", text);
        Assert.Contains("Пишем на ASP.NET Core", text);
    }

    [Fact]
    public void Vacancy_PendingUntilLlm_ThenQueuedOrSkipped()
    {
        var fit = Pending();
        Assert.Equal(VacancyStatus.Pending, fit.Status);
        fit.ApplyLlmEvaluation(LlmVerdict.Doubtful, 140, "не уверена", "PHP", "m", Now);
        Assert.Equal((VacancyStatus.Queued, 100, "PHP", "LLM сомневается: не уверена"), (fit.Status, fit.Score, fit.ResumeProfile, fit.StatusReason));
        Assert.Throws<DomainException>(() => fit.ApplyLlmEvaluation(LlmVerdict.Fit, 90, "", null, "m", Now));

        var reject = Pending();
        reject.ApplyLlmEvaluation(LlmVerdict.Reject, 10, "1С", null, "m", Now);
        Assert.Equal((VacancyStatus.Skipped, "LLM: 1С", "m"), (reject.Status, reject.StatusReason, reject.LlmModel));
    }
}
