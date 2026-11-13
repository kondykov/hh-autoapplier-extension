using System.Net;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;
using HhAutoApplier.Infrastructure.Sources.Hh;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Tests;

public class HhSourceTests
{
    // Страница hh.ru: JSON состояния лежит HTML-экранированным в <template id="HH-Lux-InitialState">
    private static string Page(string json) =>
        $"<html><body><template style=\"display:none\" id=\"HH-Lux-InitialState\">{WebUtility.HtmlEncode(json)}</template></body></html>";

    private static Vacancy Card(string id, string title) =>
        Vacancy.Create(id, title, $"https://hh.ru/vacancy/{id}", "dotnet", DateTimeOffset.UtcNow);

    private const string SearchJson = """
        {"vacancySearchResult":{"vacancies":[
          {"vacancyId":101,"name":"C# разработчик","company":{"name":"Acme"},"area":{"name":"Москва"},
           "compensation":{"from":200000,"to":300000,"currencyCode":"RUR"},
           "workFormats":[{"workFormatsElement":["REMOTE","HYBRID"]}],"userTestPresent":true},
          {"vacancyId":102,"name":"PHP разработчик","company":{"name":"Beta"},"area":{"name":"Тверь"}}
        ]}}
        """;

    private const string VacancyJson = """
        {"vacancyView":{"vacancyFull":{"vacancy":{
          "vacancyId":101,"name":"Senior C# разработчик","description":"<p>Пишем на <strong>ASP.NET</strong> &amp; PostgreSQL</p><ul><li>Docker</li></ul>",
          "keySkills":["Kafka"],"workFormats":["REMOTE"],"publicationTimeIso":"2026-10-05T21:35:34.795+03:00",
          "compensation":{"from":250000,"currencyCode":"RUR"}}}}}
        """;

    [Fact]
    public void Search_ParsesCards()
    {
        using var state = HhPageParser.ExtractState(Page(SearchJson))!;
        var list = HhPageParser.ParseSearch(state.RootElement, "dotnet", DateTimeOffset.UtcNow)!;

        Assert.Equal(["101", "102"], list.Select(v => v.Id));
        var v = list[0];
        Assert.Equal(("C# разработчик", "Acme", "Москва"), (v.Title, v.Company, v.Area));
        Assert.Equal((200000, 300000, "RUR"), (v.SalaryFrom!.Value, v.SalaryTo!.Value, v.SalaryCurrency));
        Assert.Equal("Удалённо, Гибрид", v.WorkFormat);
        Assert.True(v.HasTest);
        Assert.Equal("https://hh.ru/vacancy/101", v.Url);
        Assert.Equal("dotnet", v.SearchName);
        Assert.False(list[1].HasTest);
    }

    [Fact]
    public void Search_WithZeroResults_IgnoresFillerVacancies()
    {
        using var state = HhPageParser.ExtractState(Page("""
            {"vacancySearchResult":{"totalResults":0,"vacancies":[{"vacancyId":201,"name":"Ведущий менеджер маркетплейса"}]}}
            """))!;

        Assert.Empty(HhPageParser.ParseSearch(state.RootElement, "dotnet", DateTimeOffset.UtcNow)!);
    }

    [Fact]
    public void Vacancy_AddsDescriptionStackAndDate()
    {
        var v = Card("101", "C# разработчик");
        using var state = HhPageParser.ExtractState(Page(VacancyJson))!;

        Assert.True(HhPageParser.ParseVacancy(state.RootElement, v));
        Assert.Equal("Senior C# разработчик", v.Title);
        Assert.Contains("ASP.NET & PostgreSQL", v.Description);
        Assert.Contains("- Docker", v.Description);
        Assert.Contains("C#", v.Stack);
        Assert.Contains("Kafka", v.Stack);
        Assert.Equal("Удалённо", v.WorkFormat);
        Assert.Equal(250000, v.SalaryFrom);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 21, 35, 34, 795, TimeSpan.FromHours(3)), v.PublishedAt);
        Assert.Equal(TimeSpan.Zero, v.PublishedAt.Offset);
    }

    // Структура живых страниц 138757991 и 139198060 (2026-11-09), только нужные поля
    private const string JuniorJson = """
        {"vacancyView":{"vacancyFull":{"vacancy":{
          "vacancyId":138757991,"name":"Junior C#/.Net Developer","description":"<p>ASP.NET Core</p>",
          "workExperience":"between1And3","internship":false}}}}
        """;

    private const string InternJson = """
        {"vacancyView":{"vacancyFull":{"vacancy":{
          "vacancyId":139198060,"name":"Java разработчик/Java Developer (стажер)","description":"<p>Java</p>",
          "workExperience":"noExperience","internship":true}}}}
        """;

    private const string NoExperienceJson = """
        {"vacancyView":{"vacancyFull":{"vacancy":{
          "vacancyId":1,"name":"Junior","description":"","workExperience":"noExperience","internship":false}}}}
        """;

    [Theory]
    [InlineData(JuniorJson, "between1And3", false)]
    [InlineData(InternJson, Vacancy.Internship, true)]
    [InlineData(NoExperienceJson, Vacancy.NoExperience, true)]
    [InlineData(VacancyJson, null, false)]
    public void Vacancy_ReadsWorkExperienceAndInternship(string json, string? experience, bool entryLevel)
    {
        var v = Card("1", "x");
        using var state = HhPageParser.ExtractState(Page(json))!;

        Assert.True(HhPageParser.ParseVacancy(state.RootElement, v));

        Assert.Equal((experience, entryLevel), (v.WorkExperience, v.EntryLevel));
    }

    [Fact]
    public void PageWithoutState_OrWithoutResults_IsNotParsed()
    {
        Assert.Null(HhPageParser.ExtractState("<html>Подтвердите, что вы не робот</html>"));
        using var state = HhPageParser.ExtractState(Page("""{"other":1}"""))!;
        Assert.Null(HhPageParser.ParseSearch(state.RootElement, "x", DateTimeOffset.UtcNow));
        Assert.False(HhPageParser.ParseVacancy(state.RootElement, Card("1", "x")));
    }

    private sealed class StubHandler(HttpStatusCode status, string body = "", string? finalPath = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Имитирует редирект: итоговый адрес запроса — тот, куда привёл hh.ru
            if (finalPath is not null) request.RequestUri = new Uri(request.RequestUri!, finalPath);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body), RequestMessage = request });
        }
    }

    private static HhWebSource Source(HttpStatusCode status, string body = "", string? finalPath = null) =>
        new(new HttpClient(new StubHandler(status, body, finalPath)), Options.Create(new HhOptions()), TimeProvider.System);

    private static readonly SearchProfile Search = SearchProfile.Create("dotnet", "C#", null, null, null, true);

    [Fact]
    public async Task Source_ReturnsSearchAndDetails()
    {
        Assert.Equal(2, (await Source(HttpStatusCode.OK, Page(SearchJson)).SearchAsync(Search, 0, default)).Count);
        Assert.Equal(VacancyPageState.Available, await Source(HttpStatusCode.OK, Page(VacancyJson)).LoadDetailsAsync(Card("101", "x"), default));
        Assert.Equal(VacancyPageState.Unavailable, await Source(HttpStatusCode.NotFound).LoadDetailsAsync(Card("1", "x"), default));
    }

    // Живые страницы 138137097 (в архиве) и 138922163 (открыта), 2026-11-10: status и closedForApplicants — как есть, остальное сокращено
    private const string ArchivedJson = """
        {"vacancyView":{"vacancyFull":{"vacancy":{
          "vacancyId":138137097,"name":"Senior .NET-разработчик","description":"<p>ASP.NET Core</p>",
          "status":{"archived":true,"disabled":false,"hiddenInArchive":false,"premoderationStatus":"APPROVED","approved":true,"moderatePriorityScore":0},
          "closedForApplicants":false}}}}
        """;

    private const string OpenJson = """
        {"vacancyView":{"vacancyFull":{"vacancy":{
          "vacancyId":138922163,"name":"Разработчик ASP.NET","description":"<p>ASP.NET Core</p>",
          "status":{"archived":false,"disabled":false,"hiddenInArchive":false,"premoderationStatus":"APPROVED","approved":true,"moderatePriorityScore":0},
          "closedForApplicants":false}}}}
        """;

    [Theory]
    [InlineData(ArchivedJson, VacancyPageState.Archived)]
    [InlineData(OpenJson, VacancyPageState.Available)]
    [InlineData(VacancyJson, VacancyPageState.Available)]
    public async Task Source_ArchiveOnlyByStatusFlag(string json, VacancyPageState expected) =>
        Assert.Equal(expected, await Source(HttpStatusCode.OK, Page(json)).LoadDetailsAsync(Card("1", "x"), default));

    // Подробности разбираются и у архивной: подтверждённая карточка не должна остаться с текстом клиента
    [Fact]
    public async Task Source_ArchivedPage_StillFillsCard()
    {
        var card = Card("1", "x");

        await Source(HttpStatusCode.OK, Page(ArchivedJson)).LoadDetailsAsync(card, default);

        Assert.Equal(("Senior .NET-разработчик", "ASP.NET Core"), (card.Title, card.Description));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Source_ServerError_IsTemporary_NotUnavailable(HttpStatusCode status) =>
        Assert.Equal(VacancyPageState.TemporaryError, await Source(status).LoadDetailsAsync(Card("1", "x"), default));

    // Выдача — по-прежнему ошибка прохода: без неё сбору нечего делать
    [Fact]
    public async Task Source_ServerErrorOnSearch_StillThrows() =>
        await Assert.ThrowsAsync<HttpRequestException>(() => Source(HttpStatusCode.BadGateway).SearchAsync(Search, 0, default));

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.OK, "/account/captcha?backurl=x")]
    public async Task Source_Blocked_StopsCollection(HttpStatusCode status, string? finalPath) =>
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(status, Page(SearchJson), finalPath).SearchAsync(Search, 0, default));

    [Fact]
    public async Task Source_PageWithoutResults_IsBlocked() =>
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(HttpStatusCode.OK, "<html></html>").SearchAsync(Search, 0, default));

    // Страница без данных ничего не говорит о вакансии: отклик нельзя закрыть как «вакансия недоступна» (#59)
    [Fact]
    public async Task Source_VacancyPageWithoutState_IsBlocked_NotUnavailable() =>
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(HttpStatusCode.OK, "<html></html>").LoadDetailsAsync(Card("1", "x"), default));

    // JSON есть, вакансии нет (вход, мягкий антибот): отклик закрывать нельзя
    [Fact]
    public async Task Source_VacancyPageWithoutVacancy_IsBlocked_NotUnavailable() =>
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(HttpStatusCode.OK, Page("""{"other":1}""")).LoadDetailsAsync(Card("1", "x"), default));

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.OK, "/account/captcha?backurl=x")]
    public async Task Source_BlockedOnArchivedPage_NotArchived(HttpStatusCode status, string? finalPath) =>
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(status, Page(ArchivedJson), finalPath).LoadDetailsAsync(Card("1", "x"), default));

    // Страница вакансии и главная отвечают по-разному: главная — контрольный запрос после 403 (#99)
    private sealed class RouteHandler(HttpStatusCode vacancy, HttpStatusCode home, string? homePath = null) : HttpMessageHandler
    {
        public int HomeRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var isHome = request.RequestUri!.AbsolutePath == "/";
            if (isHome)
            {
                HomeRequests++;
                if (homePath is not null) request.RequestUri = new Uri(request.RequestUri, homePath);
            }
            var status = isHome ? home : vacancy;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(Page("""{"x":1}""")), RequestMessage = request });
        }
    }

    // Закрытая вакансия отдаёт 403 всем, а главная открывается: вакансия недоступна, сбор идёт дальше
    [Fact]
    public async Task Source_ForbiddenVacancy_HomeOpens_IsUnavailable()
    {
        var handler = new RouteHandler(HttpStatusCode.Forbidden, HttpStatusCode.OK);
        var source = new HhWebSource(new HttpClient(handler), Options.Create(new HhOptions()), TimeProvider.System);

        Assert.Equal(VacancyPageState.Unavailable, await source.LoadDetailsAsync(Card("139119374", "x"), default));
        Assert.Equal(1, handler.HomeRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.OK, "/account/captcha?backurl=x")]
    public async Task Source_ForbiddenVacancy_HomeBlocked_StopsCollection(HttpStatusCode home, string? homePath)
    {
        var handler = new RouteHandler(HttpStatusCode.Forbidden, home, homePath);
        var source = new HhWebSource(new HttpClient(handler), Options.Create(new HhOptions()), TimeProvider.System);

        await Assert.ThrowsAsync<SourceBlockedException>(() => source.LoadDetailsAsync(Card("1", "x"), default));
    }

    // 403 на выдаче — по-прежнему блокировка, без контрольного запроса
    [Fact]
    public async Task Source_ForbiddenSearch_NoHomeCheck()
    {
        var handler = new RouteHandler(HttpStatusCode.Forbidden, HttpStatusCode.OK);
        var source = new HhWebSource(new HttpClient(handler), Options.Create(new HhOptions()), TimeProvider.System);

        await Assert.ThrowsAsync<SourceBlockedException>(() => source.SearchAsync(Search, 0, default));
        Assert.Equal(0, handler.HomeRequests);
    }
}
