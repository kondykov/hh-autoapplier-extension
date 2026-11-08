using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Llm;

namespace HhAutoApplier.Tests;

// Проверки ответов модели, найденные на живом ГигаЧате (#67): сроки словами, заглушки, неполная анкета
public class LlmAnswerChecksTests
{
    [Theory]
    [InlineData("Более трёх лет разработки", "Backend-разработчик: C#", "трёх лет")]
    [InlineData("два года с PHP", "Опыт: C#", "два года")]
    [InlineData("опыт восемь месяцев", "Стаж 8 лет", "восемь месяцев")]
    public void Unsupported_FindsDurationsInWords(string text, string facts, string expected) =>
        Assert.Equal([expected], FactFigures.Unsupported(text, facts));

    [Theory]
    [InlineData("более трёх лет разработки", "Опыт 3 года на C#")]
    [InlineData("пять лет на C#", "Стаж 5+ лет")]
    [InlineData("полтора года", "1,5 года в команде")]
    [InlineData("опыт: три года", "Три года на .NET")]
    public void Unsupported_AcceptsDurationsFromFacts(string text, string facts) =>
        Assert.Empty(FactFigures.Unsupported(text, facts));

    [Fact]
    public void Unsupported_VacancyRequirementDoesNotConfirmExperience() =>
        Assert.Equal(["3 года"], FactFigures.Unsupported("У меня 3 года опыта", "Требуется опыт от 3 лет", "Backend-разработчик"));

    [Fact]
    public void Unsupported_DoesNotRepeatDigitFigure_AndIgnoresCalendarYears()
    {
        Assert.Equal(["10"], FactFigures.Unsupported("10 лет опыта", "C#"));
        Assert.Empty(FactFigures.Unsupported("С 2020 года работаю на C#", "Работаю с 2020"));
        Assert.Empty(FactFigures.Unsupported("Одна из задач и три принципа", "C#"));
    }

    [Theory]
    [InlineData("С уважением,\n[Ваше Имя]\n[Контакты]", "", new[] { "[Ваше Имя]", "[Контакты]" })]
    [InlineData("Мне удобно [удобное время]", "C#", new[] { "[удобное время]" })]
    [InlineData("С уважением, [Иван]", "Шаблон: С уважением, [Иван]", new string[0])]
    [InlineData("Письмо без скобок", "", new string[0])]
    [InlineData("С уважением, \n(контакты из шаблона)", "", new[] { "(контакты из шаблона)" })]
    [InlineData("С уважением, \r\n(без подписи)\r\n", "", new[] { "(без подписи)" })]
    [InlineData("Работал с СУБД (PostgreSQL) и очередями.", "", new string[0])]
    [InlineData("Опыт есть.\n(С уважением, Иван)", "Шаблон:\n(С уважением, Иван)", new string[0])]
    public void Placeholders_OnlyThoseNotInTemplate(string letter, string facts, string[] expected) =>
        Assert.Equal(expected, CoverLetterWriting.Placeholders(letter, facts));

    [Fact]
    public void Problem_And_Retry_NameEveryRemark()
    {
        Assert.Equal("цифры не из фактов: 5; заглушки в письме: [Имя]", CoverLetterWriting.Problem(["5"], ["[Имя]"]));
        Assert.Equal("заглушки в письме: [Имя]", CoverLetterWriting.Problem([], ["[Имя]"]));

        var request = new LlmRequest([LlmMessage.User("факты")]);
        var retry = CoverLetterWriting.Retry(request, "письмо", ["5"], ["[Имя]"]).Messages;
        Assert.Equal("assistant", retry[^2].Role);
        Assert.Contains("5", retry[^1].Content);
        Assert.Contains("[Имя]", retry[^1].Content);

        var figuresOnly = CoverLetterWriting.Retry(request, "письмо", ["5"]).Messages[^1].Content;
        Assert.DoesNotContain("заглушки", figuresOnly);
    }
}