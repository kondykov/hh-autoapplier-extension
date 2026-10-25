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
        Assert.True(await Source(HttpStatusCode.OK, Page(VacancyJson)).LoadDetailsAsync(Card("101", "x"), default));
        Assert.False(await Source(HttpStatusCode.NotFound).LoadDetailsAsync(Card("1", "x"), default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.OK, "/account/captcha?backurl=x")]
    public async Task Source_Blocked_StopsCollection(HttpStatusCode status, string? finalPath) =>
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(status, Page(SearchJson), finalPath).SearchAsync(Search, 0, default));

    [Fact]
    public async Task Source_PageWithoutResults_IsBlocked() =>
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(HttpStatusCode.OK, "<html></html>").SearchAsync(Search, 0, default));
}
