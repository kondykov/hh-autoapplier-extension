using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

// Проверка письма по фактам о кандидате (#82): цифры вакансии, обещания и ответы на её просьбы, чужие технологии.
// Настоящие письма — из живого прогона
public class CoverLetterClaimsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Promised = "готовность к тестовому заданию";

    private const string Salary = "зарплата";

    private const string Example = "решение примера";

    private static readonly ResumeProfile Resume = ResumeProfile.Create("Backend", "C#", ["c#"],
        "1 год 8 месяцев работаю с C#/.NET, ASP.NET Core, Docker и PostgreSQL. Снизило время формирования на 30–40%.");

    private static Vacancy Card(string title = "Разработчик", string company = "Acme",
        string description = "Нужны ASP.NET Core (6+) и Git, опыт от 3 лет. Решите пример 1214*9 + 3. Зарплата 300 000 ₽. Выполните тестовое задание.")
    {
        var v = Vacancy.Create("1", title, "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateCard(company, null, null, null, null, null);
        v.UpdateDetails(null, description, []);
        return v;
    }

    private static string Facts(ResumeProfile? resume = null, Vacancy? vacancy = null, CandidateProfile? profile = null) =>
        CoverLetterWriting.Facts(vacancy ?? Card(), resume ?? Resume, profile);

    private static IReadOnlyList<string> Figures(string letter, Vacancy? vacancy = null, ResumeProfile? resume = null) =>
        CoverLetterWriting.UnsupportedFigures(letter, Facts(resume, vacancy), vacancy ?? Card());

    private static IReadOnlyList<string> Technologies(string letter, Vacancy? vacancy = null, ResumeProfile? resume = null) =>
        CoverLetterClaims.Technologies(letter, Facts(resume, vacancy), vacancy ?? Card());

    private static IReadOnlyList<string> Promises(string letter, ResumeProfile? resume = null) =>
        CoverLetterClaims.Promises(letter, Facts(resume));

    private static ResumeProfile WithSummary(string summary, string? template = null) =>
        ResumeProfile.Create("Backend", "C#", ["c#"], summary, template);

    [Fact]
    public void RealLetter_SolvedExample_Rejected()
    {
        const string letter = "Готов выполнить тестовое задание; результат примера 1214*9 + 3 = 10929. Желаемая зарплата — обсуждается";

        Assert.Equal(["1214", "9", "3", "10929"], Figures(letter));
        Assert.Equal([Promised, Salary, Example], Promises(letter));
    }

    [Fact]
    public void RealLetter_PromisesWithoutFigures_Rejected()
    {
        const string letter = "Готов выполнить тестовое задание и обсудить желаемую зарплату в удобное время.";

        Assert.Empty(Figures(letter));
        Assert.Equal([Promised, Salary], Promises(letter));
    }

    [Fact]
    public void RealLetter_VersionFromVacancy_Rejected()
    {
        const string letter = "Имею опыт разработки на C# и ASP.NET Core (6+)";

        Assert.Equal(["6"], Figures(letter));
        Assert.Empty(Technologies(letter));
    }

    [Fact]
    public void RealLetter_GitNotInResume_DockerIs()
    {
        const string letter = "У меня есть базовые навыки работы с Docker и уверенное владение Git";

        Assert.Equal(["Git"], Technologies(letter));
    }

    [Theory]
    [InlineData("Это снизило время формирования на 30–40%.")]
    [InlineData("1 год 8 месяцев работаю с C#/.NET.")]
    [InlineData("Готов применить полученные навыки к задачам промышленной автоматизации, изучить MVx-архитектуру, WPF/Avalonia и Linux-окружение.")]
    public void LegitimateLetters_Pass(string letter)
    {
        Assert.Empty(Figures(letter));
        Assert.Empty(Promises(letter));
        Assert.Empty(Technologies(letter));
    }

    [Fact]
    public void Figures_VacancyNumbersAreNotFacts()
    {
        Assert.Equal(["300 000"], Figures("Рассчитываю на 300 000 ₽.").Select(f => f.Replace("300000", "300 000")).ToList());
        Assert.Equal(["3"], Figures("Работаю уже 3 года."));
    }

    [Theory]
    [InlineData("Программист 1С", "Acme", "Откликаюсь на позицию программиста 1С.")]
    [InlineData("Программист 1C", "Acme", "Откликаюсь на позицию программиста 1С.")]
    [InlineData("Разработчик", "1С-Рарус", "Хочу работать в 1С-Рарус.")]
    [InlineData("Разработчик", "2ГИС", "Хочу работать в 2ГИС.")]
    [InlineData("Разработчик", "ООО «3 Линия»", "Хочу работать в 3 Линия.")]
    [InlineData("Инженер 2 категории", "Acme", "Откликаюсь на вакансию Инженер 2 категории.")]
    public void Figures_InNamedCompanyOrRole_AreNotClaims(string title, string company, string letter) =>
        Assert.Empty(Figures(letter, Card(title, company)));

    [Fact]
    public void Figures_NameOfRoleDoesNotConfirmOtherNumbers() =>
        Assert.Equal(["5"], Figures("Откликаюсь на позицию программиста 1С, у меня 5 проектов.", Card("Программист 1С")));

    [Theory]
    [InlineData("Работал с S3, K8s, C#, .NET и HTML5.")]
    [InlineData("Готов освоить .NET 8 и изучить 1С.")]
    [InlineData("Интересно освоить 3D и Kafka 3.")]
    public void Figures_InsideTechnologyNames_AreNotClaims(string letter) =>
        Assert.Empty(Figures(letter));

    [Theory]
    [InlineData("Готова к тестовому заданию.", Promised)]
    [InlineData("Сделаю тестовое.", Promised)]
    [InlineData("Зарплатные ожидания обсудим.", Salary)]
    [InlineData("Ожидаемый уровень дохода готов обсудить.", Salary)]
    [InlineData("Ответ на пример — сорок два.", Example)]
    public void Promises_Rejected(string letter, string expected) =>
        Assert.Equal([expected], Promises(letter));

    [Theory]
    [InlineData("Настраивал тестовое окружение и писал тестовые сценарии.")]
    [InlineData("Готов к тестовому окружению заказчика.")]
    [InlineData("Автоматизировал расчёт зарплаты.")]
    [InlineData("Например, решение задач оптимизации запросов.")]
    [InlineData("Готов обсудить задачи команды.")]
    public void Promises_OrdinaryWordsPass(string letter) =>
        Assert.Empty(Promises(letter));

    [Fact]
    public void Promises_FromTemplateAndProfile_Allowed()
    {
        var template = WithSummary("C#", "Готов выполнить тестовое задание. Зарплатные ожидания обсудим при встрече.");
        const string letter = "Готов выполнить тестовое задание и обсудить зарплатные ожидания.";
        Assert.Empty(Promises(letter, template));

        var profile = CandidateProfile.Create(null);
        profile.Update("Пожелания: зарплата от 250 000 ₽, готов к тестовому заданию", []);
        Assert.Empty(CoverLetterClaims.Promises(letter, Facts(profile: profile)));
    }

    [Fact]
    public void Promises_ExampleFromTemplate_Allowed() =>
        Assert.Empty(Promises("2 + 2 = 4, как и в шаблоне.", WithSummary("C#", "Всегда пишу: 2 + 2 = 4")));

    [Theory]
    [InlineData("Опыт с Python и Go.", new[] { "Python", "Go" })]
    [InlineData("Работаю с KAFKA.", new[] { "Kafka" })]
    [InlineData("Изучил Kafka на прошлой работе.", new[] { "Kafka" })]
    [InlineData("Хочу применить опыт с Kafka.", new[] { "Kafka" })]
    [InlineData("Интересна ваша задача, настраивал Kubernetes.", new[] { "Kubernetes" })]
    [InlineData("С Kafka не работал, но Redis использую каждый день.", new[] { "Redis" })]
    [InlineData("Настраивал k8s.", new[] { "Kubernetes" })]
    public void Technologies_NotInFacts_Rejected(string letter, string[] expected) =>
        Assert.Equal(expected, Technologies(letter));

    [Theory]
    [InlineData("Использую docker и postgres каждый день.")]
    [InlineData("Работал с Django.")]
    [InlineData("Пишу на ASP.NET Core.")]
    [InlineData("Пишу на C#, знаю dotnet.")]
    public void Technologies_InFacts_SynonymsCaseAndBounds(string letter) =>
        Assert.Empty(Technologies(letter));

    [Theory]
    [InlineData("Готов освоить Kafka.")]
    [InlineData("Хочу изучить Kafka, Redis и RabbitMQ.")]
    [InlineData("Интересно поработать с Kafka.")]
    [InlineData("Kafka хочу освоить в ближайшее время.")]
    [InlineData("С Kafka не работал, но готов разобраться.")]
    [InlineData("Опыта с Kafka пока нет.")]
    [InlineData("Готов применить навыки, изучить Kafka и Python.")]
    public void Technologies_IntentOrHonestAbsence_Pass(string letter) =>
        Assert.Empty(Technologies(letter));

    [Theory]
    [InlineData(".NET Developer", "Acme", "Откликаюсь: вакансия .NET Developer мне подходит.")]
    [InlineData("Разработчик Python", "Acme", "Откликаюсь на вакансию разработчика Python.")]
    [InlineData("Go-разработчик", "Acme", "Рассматриваю позицию Go-разработчика.")]
    [InlineData("Разработчик", "Docker Inc", "Хочу работать в Docker Inc.")]
    public void Technologies_InNamedCompanyOrRole_AreNotClaims(string title, string company, string letter) =>
        Assert.Empty(Technologies(letter, Card(title, company), WithSummary("Опыт на Ruby")));

    [Fact]
    public void Technologies_RoleDoesNotCoverOtherTechnology() =>
        Assert.Equal(["Kafka"], Technologies("На вакансию разработчика Python: настраивал Kafka.", Card("Разработчик Python"), WithSummary("Опыт на Python")));

    [Fact]
    public void Technologies_VacancyTextIsNotFact() =>
        Assert.Equal(["Git"], Technologies("Работаю с Git.", Card(description: "Git, Git и снова Git")));

    [Fact]
    public void Technologies_ProfileConfirms()
    {
        var profile = CandidateProfile.Create(null);
        profile.Update(null, [new SkillFact { Skill = "Git", Facts = "ежедневно" }]);
        var vacancy = Card();

        Assert.Empty(CoverLetterClaims.Technologies("Работаю с Git.", Facts(vacancy: vacancy, profile: profile), vacancy));
    }

    [Fact]
    public void Problem_And_Retry_NameNewRemarks()
    {
        Assert.Equal("обещания не из фактов: зарплата; технологии не из фактов: Git", CoverLetterWriting.Problem([], [], [Salary], ["Git"]));

        var retry = CoverLetterWriting.Retry(new LlmRequest([LlmMessage.User("факты")]), "письмо", [], [], [Salary], ["Git"]).Messages[^1].Content;
        Assert.Contains(Salary, retry);
        Assert.Contains("Git", retry);
        Assert.Contains("готов освоить", retry);
    }
}
