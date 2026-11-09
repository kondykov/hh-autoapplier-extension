using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Tests.Api;

// «Оценить заново» и «Скрыть» (#71): файл не обращается к новым членам домена, поэтому собирается и на старом коде
[Collection(ApiCollection.Name)]
public class ReevaluateHideApiTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Search = "dotnet";

    private const string Password = "user-password-1";

    private readonly HttpClient _api = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task SaveSearch(object? rules = null, HttpClient? client = null) =>
        await Json(await (client ?? _api).PutAsJsonAsync("/api/searches", new { name = Search, query = "C#", rules = rules ?? new { } }));

    private async Task Collect() => await Json(await _api.PostAsync("/api/collect", null));

    // Вакансия из сбора в очереди (LLM на сборе выключена)
    private async Task Queued(string id = "1")
    {
        await SaveSearch();
        factory.Source.Add(id, "C# разработчик", "ASP.NET Core");
        await Collect();
    }

    private async Task Skipped(string id = "1")
    {
        await Queued(id);
        await SetStatus(id, "Skipped", "пропущено вручную");
    }

    private Task<JsonElement> SetStatus(string id, string status, string? reason = null, HttpClient? client = null) =>
        Post($"/api/vacancies/{id}/status", new { status, reason }, client);

    private async Task<JsonElement> Post(string url, object? body = null, HttpClient? client = null) =>
        await Json(body is null ? await (client ?? _api).PostAsync(url, null) : await (client ?? _api).PostAsJsonAsync(url, body));

    private Task<HttpResponseMessage> Action(string id, string action, HttpClient? client = null) =>
        (client ?? _api).PostAsync($"/api/vacancies/{id}/{action}", null);

    private async Task<List<JsonElement>> List(string query = "", HttpClient? client = null) =>
        (await Json(await (client ?? _api).GetAsync($"/api/vacancies?take=200{query}"))).EnumerateArray().ToList();

    private async Task<JsonElement> Stats() => await Json(await _api.GetAsync("/api/stats"));

    private async Task<JsonElement> Vacancy(string id) =>
        (await List("&includeHidden=true")).Single(v => v.GetProperty("id").GetString() == id);

    private async Task<string?> Status(string id) => (await Vacancy(id)).GetProperty("status").GetString();

    private Task<int> Applications(string id) => factory.WithDbAsync(db => db.JobApplications.CountAsync(a => a.VacancyId == id));

    private Task SetLetter(string id, string text, string model) => factory.WithDbAsync(async db =>
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "JobApplications" SET "CoverLetter" = {text}, "CoverLetterModel" = {model}, "CoverLetterAt" = now() WHERE "VacancyId" = {id}""");
        return 0;
    });

    private Task SetQuestions(string id) => factory.WithDbAsync(async db =>
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $$"""UPDATE "JobApplications" SET "Questions" = '[{"Text":"Опыт?","Type":"Text","Options":[],"Answer":["Пять лет"]}]'::jsonb WHERE "VacancyId" = {{id}}""");
        return 0;
    });

    [Fact]
    public async Task Reevaluate_Skipped_GoesToPending_ThenLlmVerdictReplacesOld()
    {
        await Skipped();
        factory.Llm.Configured = true;

        var result = await Json(await Action("1", "reevaluate"));

        Assert.Equal("Pending", result.GetProperty("status").GetString());
        Assert.Contains(result.GetProperty("history").EnumerateArray(), e => e.GetProperty("reason").GetString() == "повторная оценка");

        factory.Llm.Answers.Enqueue("""{"verdict": "fit", "score": 90, "resume": ".NET", "reason": "стек совпадает"}""");
        await Post("/api/llm/evaluate", new { limit = 10 });

        var evaluated = await Vacancy("1");
        Assert.Equal(("Queued", "стек совпадает"), (evaluated.GetProperty("status").GetString(), evaluated.GetProperty("llmReason").GetString()));
    }

    [Fact]
    public async Task Reevaluate_WithoutLlm_Returns503_AndChangesNothing()
    {
        await Skipped();
        var before = await Vacancy("1");

        var response = await Action("1", "reevaluate");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("LLM не настроена", await response.Content.ReadAsStringAsync());
        var after = await Vacancy("1");
        Assert.Equal(("Skipped", before.GetProperty("history").GetArrayLength()),
            (after.GetProperty("status").GetString(), after.GetProperty("history").GetArrayLength()));
    }

    [Fact]
    public async Task Reevaluate_ImportedWithoutLlm_Returns503_AndStaysSkipped()
    {
        await SaveSearch();
        await Json(await _api.PostAsJsonAsync("/api/import", new { search = Search, vacancies = new[] { new { id = "1", title = "C#", company = "Acme" } } }));
        await SetStatus("1", "Skipped", "пропущено вручную");

        var response = await Action("1", "reevaluate");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Skipped", await Status("1"));
    }

    [Fact]
    public async Task Reevaluate_Imported_GoesToNew_ThenCollectConfirmsAndEvaluates()
    {
        await SaveSearch();
        factory.Source.AddUnlisted("1", "C# разработчик", "Полное описание: ASP.NET Core");
        await Json(await _api.PostAsJsonAsync("/api/import", new { search = Search, vacancies = new[] { new { id = "1", title = "C#", company = "Acme" } } }));
        await SetStatus("1", "Skipped", "пропущено вручную");
        factory.Llm.Configured = true;

        var result = await Json(await Action("1", "reevaluate"));
        Assert.Equal("New", result.GetProperty("status").GetString());

        await Collect();

        var evaluated = await Vacancy("1");
        Assert.Equal(("Pending", "Полное описание: ASP.NET Core"), (evaluated.GetProperty("status").GetString(), evaluated.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task Reevaluate_RulesStillReject_StaysSkipped_WithRuleReason()
    {
        await Skipped();
        await SaveSearch(new { excludeKeywords = new[] { "ASP.NET" } });
        factory.Llm.Configured = true;

        var result = await Json(await Action("1", "reevaluate"));

        Assert.Equal(("Skipped", "стоп-слово «ASP.NET»"), (result.GetProperty("status").GetString(), result.GetProperty("statusReason").GetString()));
    }

    [Theory]
    [InlineData("Applied")]
    [InlineData("Invited")]
    [InlineData("Rejected")]
    public async Task Sent_ReevaluateAndHide_Return409(string status)
    {
        await Queued();
        await SetStatus("1", "Applied");
        if (status == "Rejected")
            await SetStatus("1", "Rejected", "отказ");
        if (status == "Invited")
            await factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""UPDATE "JobApplications" SET "Status" = 'Invited'"""));
        factory.Llm.Configured = true;

        Assert.Equal(HttpStatusCode.Conflict, (await Action("1", "reevaluate")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Action("1", "hide")).StatusCode);
        Assert.Equal(status, await Status("1"));
    }

    [Fact]
    public async Task Failed_ReevaluateAndHide_Return409_RequeueStillWorks()
    {
        await Queued();
        await SetStatus("1", "Failed", "ошибка");
        factory.Llm.Configured = true;

        Assert.Equal(HttpStatusCode.Conflict, (await Action("1", "reevaluate")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Action("1", "hide")).StatusCode);

        Assert.Equal("Queued", (await SetStatus("1", "Queued")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reevaluate_ResetsLlmLetterAndQuestionnaire()
    {
        await Skipped();
        await SetLetter("1", "Письмо LLM", "model");
        await SetQuestions("1");
        factory.Llm.Configured = true;

        var result = await Json(await Action("1", "reevaluate"));

        Assert.Equal(JsonValueKind.Null, result.GetProperty("coverLetter").ValueKind);
        Assert.Equal(0, result.GetProperty("questions").GetArrayLength());
        var stored = await factory.WithDbAsync(db => db.JobApplications.AsNoTracking().SingleAsync(a => a.VacancyId == "1"));
        Assert.Null(stored.CoverLetter);
        Assert.Empty(stored.Questions);
    }

    [Fact]
    public async Task Reevaluate_KeepsManualLetter()
    {
        await Skipped();
        await SetLetter("1", "Письмо человека", "manual");
        factory.Llm.Configured = true;

        var result = await Json(await Action("1", "reevaluate"));

        Assert.Equal("Письмо человека", result.GetProperty("coverLetter").GetString());
    }

    [Fact]
    public async Task Requeue_KeepsLetter()
    {
        await Skipped();
        await SetLetter("1", "Письмо LLM", "model");

        var result = await SetStatus("1", "Queued");

        Assert.Equal("Письмо LLM", result.GetProperty("coverLetter").GetString());
    }

    [Fact]
    public async Task Hidden_NotInDefaultList_ShownWithFilter_UnhideReturns_StatsAndHistoryUnchanged()
    {
        await Skipped();
        var stats = (await Stats()).GetRawText();
        var history = (await Vacancy("1")).GetProperty("history").GetRawText();

        var hidden = await Json(await Action("1", "hide"));

        Assert.NotEqual(JsonValueKind.Null, hidden.GetProperty("hiddenAt").ValueKind);
        Assert.True(hidden.GetProperty("dismissed").GetBoolean());
        Assert.Empty(await List());
        Assert.Empty(await List("&status=Skipped"));
        var shown = Assert.Single(await List("&includeHidden=true"));
        Assert.Equal("Skipped", shown.GetProperty("status").GetString());
        Assert.Equal(history, shown.GetProperty("history").GetRawText());
        Assert.Equal(stats, (await Stats()).GetRawText());

        var back = await Json(await Action("1", "unhide"));

        Assert.Equal(JsonValueKind.Null, back.GetProperty("hiddenAt").ValueKind);
        Assert.Single(await List());
        Assert.Equal(stats, (await Stats()).GetRawText());
    }

    [Fact]
    public async Task Hide_Queued_Returns409()
    {
        await Queued();

        Assert.Equal(HttpStatusCode.Conflict, (await Action("1", "hide")).StatusCode);
        Assert.Single(await List());
    }

    [Fact]
    public async Task Requeue_OfHidden_ClearsHidden()
    {
        await Skipped();
        await Json(await Action("1", "hide"));

        await SetStatus("1", "Queued");

        var card = Assert.Single(await List());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("hiddenAt").ValueKind);
    }

    [Fact]
    public async Task Reevaluate_OfHidden_ClearsHidden()
    {
        await Skipped();
        await Json(await Action("1", "hide"));
        factory.Llm.Configured = true;

        await Json(await Action("1", "reevaluate"));

        var card = Assert.Single(await List());
        Assert.Equal(("Pending", JsonValueKind.Null), (card.GetProperty("status").GetString(), card.GetProperty("hiddenAt").ValueKind));
    }

    [Fact]
    public async Task Collect_DoesNotRecreateHidden()
    {
        await Skipped();
        await Json(await Action("1", "hide"));

        await Collect();

        Assert.Equal(1, await Applications("1"));
        Assert.Empty(await List());
        Assert.Equal("Skipped", await Status("1"));
    }

    [Fact]
    public async Task Import_DoesNotRecreateHidden()
    {
        await Skipped();
        await Json(await Action("1", "hide"));

        var result = await Json(await _api.PostAsJsonAsync("/api/import", new { search = Search, vacancies = new[] { new { id = "1", title = "C#", company = "Acme" } } }));

        Assert.Equal(0, result.GetProperty("added").GetInt32());
        Assert.Equal(1, await Applications("1"));
        Assert.Empty(await List());
    }

    [Fact]
    public async Task ForeignApplication_Returns404_AndStaysUnchanged()
    {
        var user = await Json(await _api.PostAsJsonAsync("/api/users", new { email = "b@test.local", password = Password, role = "User" }));
        var b = factory.CreateLoggedInClient("b@test.local", Password);
        await Skipped();
        await Json(await Action("1", "hide"));
        factory.Llm.Configured = true;
        await SaveSearch(client: b);

        // Отклика на эту вакансию у пользователя нет, хотя вакансия общая
        Assert.Equal(HttpStatusCode.NotFound, (await Action("1", "reevaluate", b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Action("1", "unhide", b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Action("1", "hide", b)).StatusCode);

        var mine = await Vacancy("1");
        Assert.Equal("Skipped", mine.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("hiddenAt").ValueKind);

        // Администратор читает скрытое чужое только фильтром, как и своё
        await Import(b);
        await SetStatus("2", "Skipped", "пропущено вручную", b);
        await Json(await Action("2", "hide", b));
        var owner = user.GetProperty("id").GetString();
        Assert.Empty(await List($"&userId={owner}"));
        Assert.Single(await List($"&userId={owner}&includeHidden=true"));
        Assert.Empty(await List(client: b));
    }

    private async Task Import(HttpClient client)
    {
        factory.Source.AddUnlisted("2", "C# разработчик", "ASP.NET Core");
        await Json(await client.PostAsJsonAsync("/api/import", new { search = Search, vacancies = new[] { new { id = "2", title = "C#", company = "Acme" } } }));
    }
}
