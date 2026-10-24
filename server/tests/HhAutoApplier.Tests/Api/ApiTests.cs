using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HhAutoApplier.Tests.Api;

[Collection(ApiCollection.Name)]
public class ApiTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _api = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<int> SaveSearch(string name = "dotnet", params string[] exclude)
    {
        var res = await _api.PutAsJsonAsync("/api/searches", new { name, query = "C#", rules = new { excludeKeywords = exclude } });
        return (await Json(res)).GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> Import(string search, params object[] vacancies) =>
        _api.PostAsJsonAsync("/api/import", new { search, vacancies });

    private static object Card(string id, string title, string description = "", int? salaryFrom = null) =>
        new { id, title, company = "Acme", description, salaryFrom, salaryCurrency = "RUR", workFormat = "Удалённо" };

    private Task<HttpResponseMessage> SetStatus(string id, string status, string? reason = null) =>
        _api.PostAsJsonAsync($"/api/vacancies/{id}/status", new { status, reason });

    [Fact]
    public async Task ApiKey_RequiredExceptHealthAndDocs()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/stats")).StatusCode);
    }

    [Fact]
    public async Task Searches_CreateUpdateDelete()
    {
        var id = await SaveSearch();
        var updated = await Json(await _api.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "ASP.NET", area = "", extraParams = "&work_format=REMOTE" }));
        Assert.Equal(id, updated.GetProperty("id").GetInt32());
        Assert.Equal("ASP.NET", updated.GetProperty("query").GetString());
        Assert.Equal("work_format=REMOTE", updated.GetProperty("extraParams").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("area").ValueKind);

        Assert.Single((await Json(await _api.GetAsync("/api/searches"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsJsonAsync("/api/searches", new { name = "x", query = "" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync($"/api/searches/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.DeleteAsync($"/api/searches/{id}")).StatusCode);
    }

    [Fact]
    public async Task Resumes_SeededAndUpdatedByName()
    {
        var seeded = await Json(await _api.GetAsync("/api/resumes"));
        Assert.Equal([".NET", "PHP"], seeded.EnumerateArray().Select(r => r.GetProperty("name").GetString()));

        var updated = await Json(await _api.PutAsJsonAsync("/api/resumes", new { name = ".NET", match = "C# .NET", keywords = new[] { "c#", " blazor ", "C#" } }));
        Assert.Equal(["c#", "blazor"], updated.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()));
        Assert.Equal(2, (await Json(await _api.GetAsync("/api/resumes"))).GetArrayLength());
    }

    [Fact]
    public async Task Import_EvaluatesRules_SkipsKnown_RejectsUnknownSearch()
    {
        await SaveSearch("dotnet", "junior");

        var first = await Json(await Import("dotnet",
            Card("1", "Senior C# разработчик", "ASP.NET, PostgreSQL", 300_000),
            Card("2", "Junior C# разработчик"),
            Card("3", "PHP разработчик", "Laravel")));
        Assert.Equal((3, 3, 2, 1), (first.GetProperty("received").GetInt32(), first.GetProperty("added").GetInt32(),
            first.GetProperty("queued").GetInt32(), first.GetProperty("skipped").GetInt32()));

        var again = await Json(await Import("dotnet", Card("1", "Senior C# разработчик")));
        Assert.Equal(0, again.GetProperty("added").GetInt32());

        var unknown = await Import("nope", Card("9", "x"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("nope", await unknown.Content.ReadAsStringAsync());

        var skipped = await Json(await _api.GetAsync("/api/vacancies?status=Skipped"));
        Assert.Equal("стоп-слово «junior»", skipped[0].GetProperty("statusReason").GetString());
    }

    [Fact]
    public async Task Queue_BestFirst_WithResumeAndLimit()
    {
        await SaveSearch();
        await Json(await Import("dotnet",
            Card("1", "C# разработчик"),
            Card("2", "Senior C# .NET разработчик", "ASP.NET Core, PostgreSQL, Docker, Kafka", 300_000),
            Card("3", "PHP разработчик", "Laravel")));

        var queue = await Json(await _api.GetAsync("/api/queue"));
        var scores = queue.EnumerateArray().Select(v => v.GetProperty("score").GetInt32()).ToList();
        Assert.Equal(scores.OrderDescending(), scores);
        Assert.Equal("2", queue[0].GetProperty("id").GetString());
        Assert.Equal(".NET", queue[0].GetProperty("resumeProfile").GetString());

        Assert.Equal(["3"], (await Json(await _api.GetAsync("/api/queue?resume=PHP"))).EnumerateArray().Select(v => v.GetProperty("id").GetString()));
        Assert.Equal(1, (await Json(await _api.GetAsync("/api/queue?limit=1"))).GetArrayLength());
    }

    [Fact]
    public async Task Status_Transitions_AndErrors()
    {
        await SaveSearch();
        await Json(await Import("dotnet", Card("1", "C# разработчик"), Card("2", "C# разработчик")));

        var applied = await Json(await SetStatus("1", "Applied"));
        Assert.Equal("Applied", applied.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, applied.GetProperty("appliedAt").ValueKind);

        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus("1", "Applied")).StatusCode);
        Assert.Equal("Rejected", (await Json(await SetStatus("1", "Rejected", "отказ"))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus("2", "Rejected")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus("2", "New")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetStatus("404", "Skipped")).StatusCode);

        await Json(await SetStatus("2", "Skipped", "анкета"));
        Assert.Equal(0, (await Json(await _api.GetAsync("/api/queue"))).GetArrayLength());
        await Json(await SetStatus("2", "Queued"));
        Assert.Equal(1, (await Json(await _api.GetAsync("/api/queue"))).GetArrayLength());

        var stats = await Json(await _api.GetAsync("/api/stats"));
        Assert.Equal((1, 1), (stats.GetProperty("Rejected").GetInt32(), stats.GetProperty("Queued").GetInt32()));
    }

    [Fact]
    public async Task Collect_AddsNew_DoesNotRefetchKnown_StopsWhenBlocked()
    {
        await SaveSearch("dotnet", "junior");
        factory.Source.Add("10", "C# разработчик", "ASP.NET Core");
        factory.Source.Add("11", "Junior C# разработчик", "");

        var first = await Json(await _api.PostAsync("/api/collect", null));
        Assert.Equal((2, 1, 1), (first.GetProperty("added").GetInt32(), first.GetProperty("queued").GetInt32(), first.GetProperty("skipped").GetInt32()));
        Assert.Equal(2, factory.Source.DetailsLoaded);

        var second = await Json(await _api.PostAsync("/api/collect", null));
        Assert.Equal(0, second.GetProperty("added").GetInt32());
        Assert.Equal(2, factory.Source.DetailsLoaded);

        factory.Source.Blocked = true;
        var blocked = await _api.PostAsync("/api/collect", null);
        Assert.Equal(HttpStatusCode.BadGateway, blocked.StatusCode);
        Assert.Contains("капча", await blocked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Report_JsonAndCsv()
    {
        await SaveSearch();
        await Json(await Import("dotnet", Card("1", "C# разработчик", "", 250_000), Card("2", "C# разработчик")));
        await Json(await SetStatus("1", "Applied"));

        Assert.Equal(["1"], (await Json(await _api.GetAsync("/api/report"))).EnumerateArray().Select(v => v.GetProperty("id").GetString()));

        var csv = await _api.GetAsync("/api/report?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal("Вакансия;Стек;Компания;Оплата;Формат;Ссылка;Дата;Резюме", lines[0]);
        Assert.Contains("\"от 250000 RUR\"", lines[1]);
        Assert.Equal(2, lines.Length);
    }
}
