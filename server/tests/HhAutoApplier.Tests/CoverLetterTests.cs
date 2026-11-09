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

    // Живые письма 2026-10-28: «на 30–40%» из фактов стало «до 30 %» и «до 40 %»
    [Fact]
    public void UnsupportedFigures_NarrowedRangesAndInventedNumbers()
    {
        var resume = ResumeProfile.Create(".NET", "C#", ["c#"],
            "3+ года, .NET 8–10; нагрузка на master-БД снизилась на 30–40%; ~80% публичного API; latency с 300+ мс до 15–70 мс, в 4–8 раз; " +
            "8 000+ пользователей, 1.5M+ SKU; gRPC: latency −40%");
        var request = CoverLetterWriting.BuildRequest(Card(), resume);

        Assert.Empty(CoverLetterWriting.UnsupportedFigures(
            "Более 3 лет на .NET 8–10: снизил нагрузку на master на 30 – 40 %, перенёс около 80 % API, с 300+ мс до 15‑70 мс (в 4–8 раз), " +
            "более 8 000 пользователей, 1.5M SKU, latency −40%.", request));
        Assert.Equal(["30", "до 30"], CoverLetterWriting.UnsupportedFigures("Снизил нагрузку на мастер‑БД до 30 %.", request));
        // «40» в фактах есть, но про latency gRPC: «до 40» — не факт
        Assert.Equal(["до 40"], CoverLetterWriting.UnsupportedFigures("Снизил нагрузку на master до 40 %.", request));
        Assert.Equal(["более 80"], CoverLetterWriting.UnsupportedFigures("Перенёс более 80 % API.", request));
        Assert.Equal(["5", "70"], CoverLetterWriting.UnsupportedFigures("5 лет опыта, ответы за 70 мс.", request));
    }

    [Fact]
    public void Retry_AppendsLetterAndBadFigures()
    {
        var first = CoverLetterWriting.BuildRequest(Card(), DotNet);
        var retry = CoverLetterWriting.Retry(first, "Письмо с цифрой 99%", ["99"]);
        Assert.Equal(["system", "user", "assistant", "user"], retry.Messages.Select(m => m.Role));
        Assert.Contains("99", retry.Messages[^1].Content);
        Assert.Equal(first.MaxTokens, retry.MaxTokens);
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
