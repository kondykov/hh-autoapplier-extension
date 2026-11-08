using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Commands.Dialogs;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Llm;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Tests.Api;

// Доля дневного лимита LLM на пользователя и предел импорта (#56): один пользователь не расходует лимит за всех
[Collection(ApiCollection.Name)]
public class LlmQuotaTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "user-password-1";

    // Доля по умолчанию — LlmOptions.UserDailyRequestLimit
    private const int Share = 150;

    private const string Fit = """{"verdict": "fit", "score": 80, "resume": null, "reason": "подходит"}""";

    private readonly HttpClient _admin = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(Guid Id, HttpClient Client)> CreateUser(string email)
    {
        var user = await Json(await _admin.PostAsJsonAsync("/api/users", new { email, password = Password, role = "User" }));
        var client = factory.CreateLoggedInClient(email, Password);
        await Json(await client.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#" }));
        return (user.GetProperty("id").GetGuid(), client);
    }

    private static object Card(string id) => new { id, title = "C# разработчик", company = "Acme", description = "ASP.NET Core" };

    private async Task Import(HttpClient client, params string[] ids)
    {
        await Json(await client.PostAsJsonAsync("/api/import", new { search = "dotnet", vacancies = ids.Select(Card) }));
        await factory.ConfirmImportedAsync();
    }

    // Сегодняшние ответы LLM в журнале пользователя
    private Task Spend(Guid? user, int count) => factory.WithDbAsync(async db =>
    {
        for (var i = 0; i < count; i++)
            db.LlmCalls.Add(LlmCall.Succeeded("test", null, "model", "p", "r", 1, 1, DateTimeOffset.UtcNow).For(user));
        return await db.SaveChangesAsync();
    });

    private async Task<List<string?>> Ids(HttpClient client, string status) =>
        (await Json(await client.GetAsync($"/api/vacancies?status={status}&take=200"))).EnumerateArray().Select(v => v.GetProperty("id").GetString()).Order().ToList();

    private async Task Evaluate(int limit = 10) => await Json(await _admin.PostAsJsonAsync("/api/llm/evaluate", new { limit }));

    [Fact]
    public async Task Import_OverOnePage_BadRequest()
    {
        var (_, client) = await CreateUser("a@test.local");
        var cards = Enumerable.Range(1, 101).Select(i => Card(i.ToString())).ToList();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/import", new { search = "dotnet", vacancies = cards })).StatusCode);
        Assert.Empty(await Ids(client, "Queued"));

        await Json(await client.PostAsJsonAsync("/api/import", new { search = "dotnet", vacancies = cards.Take(100) }));
        await factory.ConfirmImportedAsync();
        Assert.Equal(100, (await Ids(client, "Queued")).Count);
    }

    [Fact]
    public async Task UserOverShare_WaitsNextDay_OthersContinue()
    {
        factory.Llm.Configured = true;
        var (a, clientA) = await CreateUser("a@test.local");
        var (_, clientB) = await CreateUser("b@test.local");
        await Import(clientA, "1", "2", "3");
        await Import(clientB, "4");

        // Запросы без ответа (сбой провайдера) доли не тратят: у A остаётся ровно один запрос
        await Spend(a, Share - 1);
        await factory.WithDbAsync(async db =>
        {
            db.LlmCalls.Add(LlmCall.Failed("test", null, "", "p", "сеть", DateTimeOffset.UtcNow).For(a));
            return await db.SaveChangesAsync();
        });
        factory.Llm.Answers.Enqueue(Fit);
        factory.Llm.Answers.Enqueue(Fit);
        await Evaluate();

        Assert.Equal(2, factory.Llm.Calls);
        Assert.Equal(2, (await Ids(clientA, "Pending")).Count);
        Assert.Empty(await Ids(clientB, "Pending"));

        // Доля A кончилась: старые отклики A не занимают пачку, новый отклик B оценивается первым же
        await Import(clientB, "5");
        factory.Llm.Answers.Enqueue(Fit);
        await Evaluate(limit: 1);
        Assert.Equal(3, factory.Llm.Calls);
        Assert.Empty(await Ids(clientB, "Pending"));
        Assert.Equal(2, (await Ids(clientA, "Pending")).Count);
    }

    // Все четыре прохода конвейера не трогают исчерпавшего долю; у владельца по умолчанию (null) доля своя
    [Fact]
    public async Task AllPasses_SkipExhausted()
    {
        var (a, client) = await CreateUser("a@test.local");
        await Import(client, "1");
        await Json(await client.PostAsJsonAsync("/api/vacancies/1/questionnaire", new { questions = new object[] { new { question = "Опыт?", type = "Text" } } }));
        await Import(client, "2");
        await Json(await client.PostAsJsonAsync("/api/dialogs", new
        {
            chats = new object[] { new { chatId = 1L, vacancyId = "2", title = "C#", company = "Acme", messageId = 1L, text = "Когда удобно созвониться?", at = DateTimeOffset.UtcNow, fromEmployer = true } },
        }));
        await Spend(a, Share);
        await Spend(null, Share);
        factory.Llm.Configured = true;

        await Evaluate();
        await Json(await _admin.PostAsJsonAsync("/api/llm/answers", new { limit = 10 }));
        await Json(await _admin.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }));
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AnalyzeDialogsCommandHandler>().Handle(new AnalyzeDialogsCommand { Limit = 10 });

        Assert.Equal(0, factory.Llm.Calls);
        Assert.Equal(["1"], await Ids(client, "Questionnaire"));
    }

    // На повторную попытку доли не хватило: ответы с замечаниями — на ручную проверку, не догадки в очередь и не пропуск
    [Fact]
    public async Task Questionnaire_NoShareForRetry_ToReview()
    {
        var (a, client) = await CreateUser("a@test.local");
        await Import(client, "1");
        await Json(await client.PostAsJsonAsync("/api/vacancies/1/questionnaire", new { questions = new object[] { new { question = "Опыт?", type = "Text" } } }));
        await Spend(a, Share - 1);
        factory.Llm.Configured = true;
        factory.Llm.Answers.Enqueue("""{"answers": ["10 лет"]}""");

        await Json(await _admin.PostAsJsonAsync("/api/llm/answers", new { limit = 10 }));

        Assert.Equal(1, factory.Llm.Calls);
        var review = Assert.Single((await Json(await client.GetAsync("/api/vacancies?status=Review"))).EnumerateArray());
        Assert.Equal("анкета: проверить — ответ 1: 10", review.GetProperty("statusReason").GetString());
    }

    [Fact]
    public void Usage_LimitZeroOff_DefaultOwnerCountedSeparately()
    {
        var user = Guid.NewGuid();
        var off = new LlmQuotaUsage(0, [(user, 1000), (null, 1000)]);
        Assert.Empty(off.Exhausted);
        Assert.True(off.TryTake(user));

        var usage = new LlmQuotaUsage(2, [(user, 2), (null, 1)]);
        Assert.Equal([user], usage.Exhausted);
        Assert.False(usage.TryTake(user));
        Assert.True(usage.TryTake(null));
        Assert.False(usage.TryTake(null));
        Assert.True(usage.TryTake(Guid.NewGuid()));
    }
}
