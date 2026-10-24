using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

public class CoverLetterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly ResumeProfile DotNet = ResumeProfile.Create(".NET", "C# .NET", ["c#"], "5 лет на C#, ASP.NET Core, PostgreSQL",
        "Здравствуйте!\nМеня зовут Иван, пишу на C#.\n\nС уважением, Иван, @ivan_dev");

    private static Vacancy Queued()
    {
        var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateCard("Acme", null, null, null, null, null);
        v.UpdateDetails(null, "Пишем на ASP.NET Core", []);
        v.Evaluate(VacancyEvaluator.Default, new FilterRules(), [DotNet]);
        return v;
    }

    [Fact]
    public void BuildRequest_HasResumeTemplateAndVacancy()
    {
        var text = VacancyAssessment.Serialize(CoverLetterWriting.BuildRequest(Queued(), DotNet));
        Assert.Contains("5 лет на C#", text);
        Assert.Contains("@ivan_dev", text);
        Assert.Contains("Acme", text);
        Assert.Contains("Пишем на ASP.NET Core", text);

        var withoutResume = VacancyAssessment.Serialize(CoverLetterWriting.BuildRequest(Queued(), null));
        Assert.Contains("не выбрано", withoutResume);
    }

    [Fact]
    public void Parse_StripsWrappersAndRejectsGarbage()
    {
        const string letter = "Здравствуйте! Пишу на C# пять лет, ASP.NET Core и PostgreSQL — каждый день.";
        Assert.Equal(letter, CoverLetterWriting.Parse($"\n{letter}\n"));
        Assert.Equal(letter, CoverLetterWriting.Parse($"```text\n{letter}\n```"));
        Assert.Equal(letter, CoverLetterWriting.Parse($"«{letter}»"));
        Assert.Equal("Строка один, достаточно длинная для письма.\nСтрока два.",
            CoverLetterWriting.Parse("Строка один, достаточно длинная для письма.\r\nСтрока два."));

        Assert.Null(CoverLetterWriting.Parse(""));
        Assert.Null(CoverLetterWriting.Parse("Не могу."));
        Assert.Null(CoverLetterWriting.Parse("""{"letter": "Здравствуйте! Пишу на C# пять лет, ASP.NET Core"}"""));
    }

    [Fact]
    public void Vacancy_LetterOnlyInQueue()
    {
        var v = Queued();
        Assert.Equal(VacancyStatus.Queued, v.Status);
        v.SetCoverLetter("  Здравствуйте!  ", "m", Now);
        Assert.Equal(("Здравствуйте!", "m", Now), (v.CoverLetter, v.CoverLetterModel, v.CoverLetterAt));
        Assert.Throws<DomainException>(() => v.SetCoverLetter(" ", "m", Now));

        v.MarkApplied(null, Now);
        Assert.Throws<DomainException>(() => v.SetCoverLetter("Здравствуйте!", "m", Now));
    }
}
