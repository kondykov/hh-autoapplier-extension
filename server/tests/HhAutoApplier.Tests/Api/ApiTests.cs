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

        // История отклика переживает сохранение в jsonb: создан → в очереди → отклик → отказ
        var history = (await Json(await _api.GetAsync("/api/vacancies?status=Rejected"))).EnumerateArray().Single().GetProperty("history");
        Assert.Equal(["New", "Queued", "Applied", "Rejected"], history.EnumerateArray().Select(e => e.GetProperty("to").GetString()));
        Assert.Equal("отказ", history[3].GetProperty("reason").GetString());
        Assert.Equal("Applied", history[3].GetProperty("from").GetString());
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
    public async Task LlmCheck_WithoutKey_Is503_AndDoesNotCallProvider()
    {
        var res = await _api.PostAsync("/api/llm/check", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("LLM_API_KEY", await res.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Llm.Calls);
    }

    [Fact]
    public async Task LlmEvaluate_PendingGoToQueueOrSkipped_StopsOnLimit()
    {
        factory.Llm.Configured = true;
        factory.Llm.Answers.Enqueue("""{"verdict": "fit", "score": 90, "resume": ".NET", "reason": "стек совпадает"}""");
        factory.Llm.Answers.Enqueue("""Думаю так: {"verdict": "reject", "score": 15, "resume": null, "reason": "1С"}""");
        await SaveSearch();
        await Json(await Import("dotnet", Card("1", "C# разработчик"), Card("2", "C# и 1С разработчик"), Card("3", "C# разработчик")));

        // С настроенной LLM прошедшие правила ждут оценки и в очередь расширения не попадают
        Assert.Equal(0, (await Json(await _api.GetAsync("/api/queue"))).GetArrayLength());

        var res = await Json(await _api.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));
        Assert.Equal((2, 1, 1, 1), (res.GetProperty("evaluated").GetInt32(), res.GetProperty("fit").GetInt32(),
            res.GetProperty("rejected").GetInt32(), res.GetProperty("remaining").GetInt32()));
        Assert.Contains("лимит", res.GetProperty("stoppedBy").GetString());

        var all = (await Json(await _api.GetAsync("/api/vacancies"))).EnumerateArray().ToDictionary(v => v.GetProperty("id").GetString()!);
        var v1 = all["1"];
        Assert.Equal(("Queued", "Fit", 90), (v1.GetProperty("status").GetString(), v1.GetProperty("llmVerdict").GetString(), v1.GetProperty("score").GetInt32()));
        var v2 = all["2"];
        Assert.Equal(("Skipped", "LLM: 1С"), (v2.GetProperty("status").GetString(), v2.GetProperty("statusReason").GetString()));
        Assert.Equal("Pending", all["3"].GetProperty("status").GetString());

        // Журнал: два ответа и одна неудача из-за лимита
        var calls = await factory.WithDbAsync(db => Task.FromResult(db.LlmCalls.Select(c => c.Error == null).ToList()));
        Assert.Equal([true, true, false], calls);
    }

    [Fact]
    public async Task LlmLetters_ForQueuedWithoutLetter_ByResumeTemplate()
    {
        await SaveSearch();
        await Json(await _api.PutAsJsonAsync("/api/resumes", new { name = ".NET", match = "C# .NET", keywords = new[] { "c#" }, letterTemplate = "С уважением, Иван" }));
        // Без LLM на импорте вакансии сразу в очереди
        await Json(await Import("dotnet", Card("1", "C# разработчик"), Card("2", "C# разработчик"), Card("3", "C# разработчик")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _api.PostAsJsonAsync("/api/llm/letters", new { })).StatusCode);

        factory.Llm.Configured = true;
        const string letter = "Здравствуйте! Пишу на C# пять лет, ASP.NET Core знаю хорошо.\n\nС уважением, Иван";
        factory.Llm.Answers.Enqueue(letter);
        factory.Llm.Answers.Enqueue("Не могу.");
        var res = await Json(await _api.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }));
        Assert.Equal((1, 1, 2), (res.GetProperty("written").GetInt32(), res.GetProperty("failed").GetInt32(), res.GetProperty("remaining").GetInt32()));
        Assert.Contains("лимит", res.GetProperty("stoppedBy").GetString());

        // Письмо уходит в расширение вместе с очередью
        var letters = (await Json(await _api.GetAsync("/api/queue"))).EnumerateArray()
            .Select(v => v.GetProperty("coverLetter")).Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()).ToList();
        Assert.Equal([letter], letters);

        var prompts = await factory.WithDbAsync(db => Task.FromResult(db.LlmCalls.Where(c => c.Purpose == "cover-letter").Select(c => c.Prompt).ToList()));
        Assert.Equal(3, prompts.Count);
        Assert.All(prompts, p => Assert.Contains("С уважением, Иван", p));

        // Сброс и повтор: письмо очереди пишется заново
        var reset = await Json(await _api.PostAsJsonAsync("/api/llm/letters/reset", new { }));
        Assert.Equal(1, reset.GetProperty("reset").GetInt32());
        factory.Llm.Answers.Enqueue("Здравствуйте! Переписанное письмо под вакансию, без выдуманных фактов.\n\nС уважением, Иван");
        factory.Llm.Answers.Enqueue("Здравствуйте! Второе письмо под вакансию, без выдуманных фактов.\n\nС уважением, Иван");
        factory.Llm.Answers.Enqueue("Здравствуйте! Третье письмо под вакансию, без выдуманных фактов.\n\nС уважением, Иван");
        var rewritten = await Json(await _api.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }));
        Assert.Equal((3, 0), (rewritten.GetProperty("written").GetInt32(), rewritten.GetProperty("remaining").GetInt32()));
        var all = (await Json(await _api.GetAsync("/api/queue"))).EnumerateArray().Select(v => v.GetProperty("coverLetter").GetString()).ToList();
        Assert.DoesNotContain(letter, all);
        Assert.Equal(0, (await Json(await _api.PostAsJsonAsync("/api/llm/letters/reset", new { vacancyIds = new[] { "404" } }))).GetProperty("reset").GetInt32());
    }

    [Fact]
    public async Task Questionnaire_FromExtension_AnsweredByLlm_BackToQueue()
    {
        await SaveSearch();
        await Json(await Import("dotnet", Card("1", "C# разработчик"), Card("2", "C# разработчик")));

        // Расширение встретило анкеты: вакансии уходят из очереди ждать ответов
        object Questions(string option) => new
        {
            questions = new object[]
            {
                new { question = "Какой принцип SOLID про абстракции?", type = "Radio", options = new[] { "SRP", option } },
                new { question = "Зарплатные ожидания?", type = "Text" },
            },
        };
        var submitted = await Json(await _api.PostAsJsonAsync("/api/vacancies/1/questionnaire", Questions("DIP")));
        Assert.Equal("Questionnaire", submitted.GetProperty("status").GetString());
        await Json(await _api.PostAsJsonAsync("/api/vacancies/2/questionnaire", Questions("DIP")));
        Assert.Equal(HttpStatusCode.NotFound, (await _api.PostAsJsonAsync("/api/vacancies/404/questionnaire", Questions("DIP"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsJsonAsync("/api/vacancies/1/questionnaire", new { questions = Array.Empty<object>() })).StatusCode);
        Assert.Equal(0, (await Json(await _api.GetAsync("/api/queue"))).GetArrayLength());

        // Порядок — по баллу, у обеих он одинаковый: подходящий ответ годится для любой, неподходящий вариант — пропуск
        factory.Llm.Configured = true;
        factory.Llm.Answers.Enqueue("""{"answers": ["dip", "от 250 000 ₽"]}""");
        factory.Llm.Answers.Enqueue("""{"answers": ["LSP", "готов обсудить"]}""");
        var res = await Json(await _api.PostAsJsonAsync("/api/llm/answers", new { limit = 10 }));
        Assert.Equal((1, 1, 0), (res.GetProperty("answered").GetInt32(), res.GetProperty("failed").GetInt32(), res.GetProperty("remaining").GetInt32()));

        var queued = Assert.Single((await Json(await _api.GetAsync("/api/queue"))).EnumerateArray());
        var answers = queued.GetProperty("questions").EnumerateArray()
            .Select(q => (q.GetProperty("type").GetString(), q.GetProperty("answer")[0].GetString())).ToList();
        Assert.Equal([("Radio", "DIP"), ("Text", "от 250 000 ₽")], answers);

        var skipped = Assert.Single((await Json(await _api.GetAsync("/api/vacancies?status=Skipped"))).EnumerateArray());
        Assert.Contains("LSP", skipped.GetProperty("statusReason").GetString());
    }

    [Fact]
    public async Task Review_DoubtfulWaitForHuman_ApproveWithLetterOrDecline()
    {
        factory.Llm.Configured = true;
        await SaveSearch();
        await Json(await _api.PutAsJsonAsync("/api/searches", new { name = "trusted", query = "C#", rules = new { skipReview = true } }));
        await Json(await Import("dotnet", Card("1", "C# разработчик"), Card("2", "C# разработчик")));
        await Json(await Import("trusted", Card("3", "C# разработчик")));
        for (var i = 0; i < 3; i++)
            factory.Llm.Answers.Enqueue("""{"verdict": "doubtful", "score": 55, "resume": ".NET", "reason": "мало опыта"}""");
        await Json(await _api.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));

        // Сомнительные ждут человека; поиск «без проверки» — сразу в очередь
        Assert.Equal(["3"], (await Json(await _api.GetAsync("/api/queue"))).EnumerateArray().Select(v => v.GetProperty("id").GetString()));
        var review = (await Json(await _api.GetAsync("/api/vacancies?status=Review"))).EnumerateArray().Select(v => v.GetProperty("id").GetString()).Order();
        Assert.Equal(["1", "2"], review);

        var approved = await Json(await _api.PostAsJsonAsync("/api/vacancies/1/review", new { approve = true, coverLetter = "Моё письмо" }));
        Assert.Equal(("Queued", "Моё письмо", "manual"), (approved.GetProperty("status").GetString(),
            approved.GetProperty("coverLetter").GetString(), approved.GetProperty("coverLetterModel").GetString()));
        var declined = await Json(await _api.PostAsJsonAsync("/api/vacancies/2/review", new { approve = false, reason = "не мой стек" }));
        Assert.Equal(("Skipped", "отклонено вручную: не мой стек"), (declined.GetProperty("status").GetString(), declined.GetProperty("statusReason").GetString()));

        Assert.Equal(HttpStatusCode.Conflict, (await _api.PostAsJsonAsync("/api/vacancies/2/review", new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.PostAsJsonAsync("/api/vacancies/404/review", new { approve = true })).StatusCode);
    }

    [Fact]
    public async Task ReviewPage_ServedWithoutKey()
    {
        var page = await factory.CreateClient().GetAsync("/review.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Ручная проверка", await page.Content.ReadAsStringAsync());
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
