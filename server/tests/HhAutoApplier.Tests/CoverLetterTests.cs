using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
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

    private static Vacancy Card()
    {
        var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateCard("Acme", null, null, null, null, null);
        v.UpdateDetails(null, "Пишем на ASP.NET Core", []);
        return v;
    }

    private static JobApplication Queued()
    {
        var a = JobApplication.Create(Card(), null, Now);
        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), [DotNet], Now);
        return a;
    }

    [Fact]
    public void BuildRequest_HasResumeTemplateAndVacancy()
    {
        var text = VacancyAssessment.Serialize(CoverLetterWriting.BuildRequest(Card(), DotNet));
        Assert.Contains("5 лет на C#", text);
        Assert.Contains("@ivan_dev", text);
        Assert.Contains("Acme", text);
        Assert.Contains("Пишем на ASP.NET Core", text);

        var withoutResume = VacancyAssessment.Serialize(CoverLetterWriting.BuildRequest(Card(), null));
        Assert.Contains("не выбрано", withoutResume);

        // Без описания и шаблона — ни имени, ни стажа: ключевые слова не выдаются за опыт
        var bare = ResumeProfile.Create("PHP", "PHP", ["php", "laravel"]);
        var text2 = VacancyAssessment.Serialize(CoverLetterWriting.BuildRequest(Card(), bare));
        Assert.Contains("опыт не известен", text2);
        Assert.Contains("Шаблона письма нет — пиши без имени, подписи и контактов", text2);
        Assert.Contains("имя кандидата, подпись и контакты — только из шаблона", text2);
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
    public void Application_LetterOnlyInQueue()
    {
        var v = Queued();
        Assert.Equal(VacancyStatus.Queued, v.Status);
        v.SetCoverLetter("  Здравствуйте!  ", "m", Now);
        Assert.Equal(("Здравствуйте!", "m", Now), (v.CoverLetter, v.CoverLetterModel, v.CoverLetterAt));
        Assert.Throws<DomainException>(() => v.SetCoverLetter(" ", "m", Now));

        v.ResetCoverLetter();
        Assert.Equal((null, null, null), (v.CoverLetter, v.CoverLetterModel, v.CoverLetterAt));
        v.SetCoverLetter("Здравствуйте заново!", "m2", Now);

        v.MarkApplied(null, Now);
        Assert.Throws<DomainException>(() => v.SetCoverLetter("Здравствуйте!", "m", Now));
        // Письмо отправленного отклика — история
        Assert.Throws<DomainException>(v.ResetCoverLetter);
        Assert.Equal("Здравствуйте заново!", v.CoverLetter);
    }
}
