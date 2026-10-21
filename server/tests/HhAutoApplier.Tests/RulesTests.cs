using HhAutoApplier.Core.Domain;
using HhAutoApplier.Core.Rules;

namespace HhAutoApplier.Tests;

public class RulesTests
{
    private static readonly ResumeProfile[] Resumes =
    [
        new() { Name = ".NET", Match = "C# .NET", Keywords = ["c#", ".net", "asp.net"] },
        new() { Name = "PHP", Match = "PHP", Keywords = ["php", "laravel"] },
    ];

    private static Vacancy V(string title, string desc = "", int? from = null, string cur = "RUR", string fmt = "") =>
        new() { Id = "1", Title = title, Description = desc, SalaryFrom = from, SalaryCurrency = cur, WorkFormat = fmt };

    [Fact]
    public void Picker_ChoosesByKeywords_NullOnTieOrNone()
    {
        Assert.Equal(".NET", ResumePicker.Pick(Resumes, "Backend C# .NET developer")?.Name);
        Assert.Equal("PHP", ResumePicker.Pick(Resumes, "PHP Laravel")?.Name);
        Assert.Null(ResumePicker.Pick(Resumes, "php и c#"));
        Assert.Null(ResumePicker.Pick(Resumes, "Менеджер"));
    }

    [Fact]
    public void Stack_ExtractedFromTextAndSkills()
    {
        var stack = StackExtractor.Extract("Разработчик на C# и ASP.NET, PostgreSQL, Docker", ["Kafka"]);
        Assert.Contains("C#", stack);
        Assert.Contains(".NET", stack);
        Assert.Contains("SQL", stack);
        Assert.Contains("Kafka", stack);
        Assert.DoesNotContain("Java", stack);
    }

    [Fact]
    public void Evaluate_ExcludeKeyword_Skips()
    {
        var rules = new FilterRules { ExcludeKeywords = ["Стажёр"] };
        var e = VacancyEvaluator.Default.Evaluate(V("Стажер C#"), rules, Resumes);
        Assert.Equal(VacancyStatus.Skipped, e.Status);
    }

    [Fact]
    public void Evaluate_SalaryConvertedToRub()
    {
        var rules = new FilterRules { MinSalaryRub = 200_000 };
        Assert.Equal(VacancyStatus.Queued, VacancyEvaluator.Default.Evaluate(V("C#", from: 3000, cur: "USD"), rules, Resumes).Status);
        Assert.Equal(VacancyStatus.Skipped, VacancyEvaluator.Default.Evaluate(V("C#", from: 100_000), rules, Resumes).Status);
        Assert.Equal(VacancyStatus.Queued, VacancyEvaluator.Default.Evaluate(V("C#"), rules, Resumes).Status);
        rules.RequireSalary = true;
        Assert.Equal(VacancyStatus.Skipped, VacancyEvaluator.Default.Evaluate(V("C#"), rules, Resumes).Status);
    }

    [Fact]
    public void Evaluate_FormatAndResume()
    {
        var rules = new FilterRules { AllowedFormats = ["удален"] };
        Assert.Equal(VacancyStatus.Skipped, VacancyEvaluator.Default.Evaluate(V("C#", fmt: "На месте работодателя"), rules, Resumes).Status);
        var ok = VacancyEvaluator.Default.Evaluate(V("C# .NET", fmt: "Удалённо"), rules, Resumes);
        Assert.Equal(VacancyStatus.Queued, ok.Status);
        Assert.Equal(".NET", ok.ResumeProfile);
    }
}
