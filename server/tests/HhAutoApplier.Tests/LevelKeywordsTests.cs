using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

// «Junior» в названии — не всегда junior (#72, часть 1)
public class LevelKeywordsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly ResumeProfile[] Resumes = [ResumeProfile.Create(".NET", "C# .NET", ["c#"], "5 лет на C#")];

    private static readonly FilterRules Level = new() { LevelKeywords = ["junior"] };

    private static Vacancy V(string title, string? experience, string description = "Пишем на ASP.NET Core")
    {
        var v = Vacancy.Create("1", title, "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateDetails(null, description, [], experience);
        return v;
    }

    private static Evaluation Evaluate(Vacancy v, FilterRules rules) => VacancyEvaluator.Default.Evaluate(v, rules, Resumes);

    private static string UserPrompt(Vacancy v, FilterRules rules) =>
        VacancyAssessment.BuildRequest(v, Resumes, rules).Messages[1].Content;

    [Theory]
    [InlineData("between1And3")]
    [InlineData("between3And6")]
    [InlineData(null)]
    public void LevelWord_WithoutExplicitEntrySign_ReachesLlm(string? experience)
    {
        var a = JobApplication.Create(V("Junior C#/.Net Developer", experience), null, Now);

        a.Evaluate(VacancyEvaluator.Default, Level, Resumes, Now, awaitLlm: true);

        Assert.Equal(VacancyStatus.Pending, a.Status);
    }

    [Theory]
    [InlineData(Vacancy.NoExperience, "слово уровня «junior»: без опыта")]
    [InlineData(Vacancy.Internship, "слово уровня «junior»: стажировка")]
    public void LevelWord_WithExplicitEntrySign_Skipped(string experience, string reason)
    {
        var e = Evaluate(V("Junior C#/.Net Developer", experience), Level);

        Assert.Equal((VacancyStatus.Skipped, reason), (e.Status, e.Reason));
    }

    [Fact]
    public void LevelWord_IsSearchedOnlyInTitle()
    {
        var v = V("C# разработчик", Vacancy.NoExperience, "Наставничество junior-разработчиков");

        Assert.Equal(VacancyStatus.Queued, Evaluate(v, Level).Status);
    }

    [Fact]
    public void EntrySign_WithoutLevelWord_DoesNotSkip() =>
        Assert.Equal(VacancyStatus.Queued, Evaluate(V("C# разработчик", Vacancy.Internship), Level).Status);

    [Fact]
    public void BlankLevelWord_MatchesNothing() =>
        Assert.Equal(VacancyStatus.Queued, Evaluate(V("C# разработчик", Vacancy.NoExperience), new FilterRules { LevelKeywords = ["", "  "] }).Status);

    [Fact]
    public void ExcludeKeyword_StillSkipsByTitleDescriptionAndStack_WhateverTheExperience()
    {
        var rules = new FilterRules { ExcludeKeywords = ["junior"] };

        foreach (var (title, description) in new[] { ("Junior C#", "Пишем"), ("C# разработчик", "Ищем junior") })
        {
            var e = Evaluate(V(title, "between1And3", description), rules);
            Assert.Equal((VacancyStatus.Skipped, "стоп-слово «junior»"), (e.Status, e.Reason));
        }
    }

    [Theory]
    [InlineData("between1And3", "Требуемый опыт: 1–3 года")]
    [InlineData("moreThan6", "Требуемый опыт: более 6 лет")]
    [InlineData(null, "Требуемый опыт: не указан")]
    public void Prompt_HasRequiredExperience(string? experience, string line) =>
        Assert.Contains(line, UserPrompt(V("Junior C#", experience), Level));

    [Fact]
    public void Prompt_TellsToCompareTitleWithRequirements_DoubtfulNotReject()
    {
        var system = VacancyAssessment.BuildRequest(V("Junior C#", "between1And3"), Resumes, Level).Messages[0].Content;

        Assert.Contains("Сравни требуемый опыт и требования с названием", system);
        Assert.Contains("повод для doubtful, а не для reject", system);
    }

    [Fact]
    public void Prompt_DoesNotListLevelWordsAsNotInteresting()
    {
        // Слово могло остаться и в стоп-словах: в пожелания оно всё равно не идёт, остальные стоп-слова идут
        var rules = new FilterRules { LevelKeywords = ["junior"], ExcludeKeywords = ["JUNIOR", "1С"] };

        var prompt = UserPrompt(V("C# разработчик", "between1And3"), rules);

        Assert.Contains("не интересно: 1С", prompt);
        Assert.DoesNotContain("junior", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_WithOnlyLevelWords_HasNoNotInterestingLine() =>
        Assert.DoesNotContain("не интересно", UserPrompt(V("C#", "between1And3"), Level));

    [Fact]
    public void AcceptSourceCard_KeepsWorkExperienceFromSource()
    {
        var imported = Vacancy.Create("1", "Junior C#", "", "dotnet", Now, imported: true);
        Assert.Null(imported.WorkExperience);

        imported.AcceptSourceCard(V("Junior C#", "between1And3"));

        Assert.Equal(("between1And3", "1–3 года"), (imported.WorkExperience, imported.WorkExperienceText));
    }
}
