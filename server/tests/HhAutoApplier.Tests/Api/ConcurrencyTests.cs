using System.Net.Http.Json;
using System.Text.Json;

namespace HhAutoApplier.Tests.Api;

// #54: LLM-конвейер ждёт ответа секундами — решение пользователя за это время не затирается
[Collection(ApiCollection.Name)]
public class ConcurrencyTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _api = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task Pending(params string[] ids)
    {
        await Json(await _api.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#", rules = new { } }));
        var cards = ids.Select(id => new { id, title = "C# разработчик", company = "Acme", description = "", workFormat = "Удалённо" }).ToArray();
        factory.Llm.Configured = true;
        await Json(await _api.PostAsJsonAsync("/api/import", new { search = "dotnet", vacancies = cards }));
        await factory.ConfirmImportedAsync();
    }

    private async Task<JsonElement> Vacancy(string id) =>
        (await Json(await _api.GetAsync("/api/vacancies"))).EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);

    [Fact]
    public async Task SkippedWhileLlmEvaluates_StaysSkipped_CallStillLogged()
    {
        await Pending("1");
        factory.Llm.Answers.Enqueue("""{"verdict": "fit", "score": 95, "resume": null, "reason": "подходит"}""");
        // Пока LLM оценивает, пользователь пропускает вакансию в веб-интерфейсе
        factory.Llm.WhileThinking = async () =>
            await Json(await _api.PostAsJsonAsync("/api/vacancies/1/status", new { status = "Skipped", reason = "сам решил" }));

        var res = await Json(await _api.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));

        Assert.Equal(0, res.GetProperty("evaluated").GetInt32());
        var v = await Vacancy("1");
        Assert.Equal(("Skipped", "сам решил"), (v.GetProperty("status").GetString(), v.GetProperty("statusReason").GetString()));
        Assert.Empty((await Json(await _api.GetAsync("/api/queue"))).EnumerateArray());
        // Вызов LLM — в журнале: на нём держится дневной лимит
        Assert.Equal(1, await factory.WithDbAsync(db => Task.FromResult(db.LlmCalls.Count())));
    }

    [Fact]
    public async Task ApprovedWithOwnLetterWhileLlmWrites_KeepsManualLetter()
    {
        await Pending("1");
        factory.Llm.Answers.Enqueue("""{"verdict": "doubtful", "score": 60, "resume": null, "reason": "проверить"}""");
        await Json(await _api.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));
        Assert.Equal("Review", (await Vacancy("1")).GetProperty("status").GetString());

        factory.Llm.Answers.Enqueue("Здравствуйте! Письмо от LLM под вакансию, без выдуманных фактов.");
        factory.Llm.WhileThinking = async () =>
            await Json(await _api.PostAsJsonAsync("/api/vacancies/1/review", new { approve = true, coverLetter = "Моё письмо" }));

        var res = await Json(await _api.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }));

        Assert.Equal(0, res.GetProperty("written").GetInt32());
        var queued = (await Json(await _api.GetAsync("/api/queue"))).EnumerateArray().Single();
        Assert.Equal("Моё письмо", queued.GetProperty("coverLetter").GetString());
    }

    [Fact]
    public async Task ManualEvaluateDuringPass_WaitsForIt_NoDoubleLlmCalls()
    {
        await Pending("1");
        factory.Llm.Answers.Enqueue("""{"verdict": "fit", "score": 95, "resume": null, "reason": "подходит"}""");
        Task<HttpResponseMessage>? second = null;
        // Второй проход начинается, пока первый ждёт LLM: он ждёт замка и видит, что оценивать уже нечего
        factory.Llm.WhileThinking = async () =>
        {
            factory.Llm.WhileThinking = null;
            second = _api.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 });
            await Task.Delay(300);
            Assert.False(second.IsCompleted);
        };

        var first = await Json(await _api.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));
        var other = await Json(await second!);

        Assert.Equal((1, 0), (first.GetProperty("evaluated").GetInt32(), other.GetProperty("evaluated").GetInt32()));
        Assert.Equal(1, factory.Llm.Calls);
        Assert.Equal("Queued", (await Vacancy("1")).GetProperty("status").GetString());
    }
}
