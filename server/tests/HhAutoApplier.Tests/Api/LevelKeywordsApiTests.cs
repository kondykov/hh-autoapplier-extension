using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Tests.Api;

// «Junior» в названии — не всегда junior (#72, часть 1): от выдачи до причины на карточке
[Collection(ApiCollection.Name)]
public class LevelKeywordsApiTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _api = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> SaveSearch(object rules) =>
        await Json(await _api.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#", rules }));

    private async Task<JsonElement> Vacancy(string id) =>
        (await Json(await _api.GetAsync("/api/vacancies"))).EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Junior_WithExperience_ReachesLlm_ReasonOnCard_EntryLevelSkipped()
    {
        var saved = await SaveSearch(new { levelKeywords = new[] { "junior" }, excludeKeywords = new[] { "1С" } });
        Assert.Equal("junior", saved.GetProperty("rules").GetProperty("levelKeywords")[0].GetString());
        factory.Llm.Configured = true;
        factory.Source.Add("1", "Junior C#/.Net Developer", "ASP.NET Core, Docker");
        factory.Source.Add("2", "Junior C# разработчик", "Обучим");
        factory.Source.Add("3", "Стажёр-junior C#", "Обучим");
        factory.Source.WorkExperience["1"] = "between1And3";
        factory.Source.WorkExperience["2"] = "noExperience";
        factory.Source.WorkExperience["3"] = "internship";

        await Json(await _api.PostAsync("/api/collect", null));

        Assert.Equal(("Pending", "1–3 года"), ((await Vacancy("1")).GetProperty("status").GetString(), (await Vacancy("1")).GetProperty("workExperience").GetString()));
        var entry = await Vacancy("2");
        Assert.Equal(("Skipped", "слово уровня «junior»: без опыта"), (entry.GetProperty("status").GetString(), entry.GetProperty("statusReason").GetString()));
        Assert.Equal("слово уровня «junior»: стажировка", (await Vacancy("3")).GetProperty("statusReason").GetString());

        factory.Llm.Answers.Enqueue("""{"verdict": "doubtful", "score": 55, "resume": null, "reason": "в названии Junior, но опыт 1–3 года и требования выше"}""");
        await Json(await _api.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));

        var request = Assert.Single(factory.Llm.Requests);
        Assert.Contains("Требуемый опыт: 1–3 года", request.Messages[1].Content);
        Assert.Contains("не интересно: 1С", request.Messages[1].Content);
        Assert.DoesNotContain("не интересно: junior", request.Messages[1].Content);

        var card = await Vacancy("1");
        Assert.Equal("Review", card.GetProperty("status").GetString());
        Assert.Equal("LLM сомневается: в названии Junior, но опыт 1–3 года и требования выше", card.GetProperty("statusReason").GetString());
    }

    // Поле правил без SQL: у сохранённых раньше поисков его нет в JSON
    [Fact]
    public async Task SearchSavedBeforeLevelKeywords_StillLoadsAndEvaluates()
    {
        await SaveSearch(new { excludeKeywords = new[] { "1С" } });
        await factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""UPDATE "Searches" SET "Rules" = "Rules" - 'LevelKeywords'"""));
        factory.Source.Add("1", "Junior C#", "ASP.NET Core");
        factory.Source.WorkExperience["1"] = "noExperience";

        var search = (await Json(await _api.GetAsync("/api/searches")))[0];
        await Json(await _api.PostAsync("/api/collect", null));

        Assert.Equal(0, search.GetProperty("rules").GetProperty("levelKeywords").GetArrayLength());
        Assert.Equal("Queued", (await Vacancy("1")).GetProperty("status").GetString());
    }

    // Прежний клиент не присылает новое поле
    [Fact]
    public async Task SaveSearch_WithoutLevelKeywords_Works() =>
        Assert.Equal(0, (await SaveSearch(new { minScore = 0 })).GetProperty("rules").GetProperty("levelKeywords").GetArrayLength());
}
