using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Commands.Notifications;
using HhAutoApplier.Application.Commands.Telegram;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Tests.Api;

// Негодные ответы модели (#67): повтор, ручная проверка вместо отправки или пропуска, сообщение администратору при частых отказах
[Collection(ApiCollection.Name)]
public class LlmChecksApiTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Good = "Здравствуйте! Заинтересовала ваша вакансия, есть опыт backend-разработки на C#.";

    private const string WithPlaceholder = "Здравствуйте! Заинтересовала ваша вакансия, есть опыт на C#.\nС уважением,\n[Ваше Имя]";

    private readonly HttpClient _api = factory.CreateApiClient();

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await Json(await _api.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#" }));
        await Json(await _api.PutAsJsonAsync("/api/resumes", new { name = ".NET", match = "C#", keywords = new[] { "c#" }, summary = "Backend-разработчик: C#." }));
        // Без LLM подтверждённая вакансия сразу в очереди
        await Json(await _api.PostAsJsonAsync("/api/import", new
        {
            search = "dotnet",
            vacancies = new[] { new { id = "1", title = "C# разработчик", company = "Acme", description = "Нужен опыт от 3 лет", salaryCurrency = "RUR", workFormat = "Удалённо" } },
        }));
        await factory.ConfirmImportedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<JsonElement> WriteLetters(params string[] answers)
    {
        factory.Llm.Configured = true;
        foreach (var answer in answers)
            factory.Llm.Answers.Enqueue(answer);
        return await Json(await _api.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }));
    }

    private async Task<JsonElement> Single(string status) =>
        Assert.Single((await Json(await _api.GetAsync($"/api/vacancies?status={status}"))).EnumerateArray());

    private async Task SubmitQuestionnaire() =>
        await Json(await _api.PostAsJsonAsync("/api/vacancies/1/questionnaire", new
        {
            questions = new object[]
            {
                new { question = "Какой принцип SOLID про абстракции?", type = "Radio", options = new[] { "SRP", "DIP" } },
                new { question = "Расскажите об опыте", type = "Text" },
            },
        }));

    private async Task<JsonElement> Answer(params string[] answers)
    {
        factory.Llm.Configured = true;
        foreach (var answer in answers)
            factory.Llm.Answers.Enqueue(answer);
        return await Json(await _api.PostAsJsonAsync("/api/llm/answers", new { limit = 10 }));
    }

    [Fact]
    public async Task Letter_WithPlaceholder_IsRetried_ThenAccepted()
    {
        var result = await WriteLetters(WithPlaceholder, Good);

        Assert.Equal(1, result.GetProperty("written").GetInt32());
        Assert.Equal(2, factory.Llm.Calls);
        Assert.Contains("[Ваше Имя]", factory.Llm.Requests[1].Messages[^1].Content);
        Assert.Equal(Good, (await Single("Queued")).GetProperty("coverLetter").GetString());
    }

    [Fact]
    public async Task Letter_WithPlaceholderTwice_GoesToReview_WithoutLetter_AndIsNotRewritten()
    {
        var result = await WriteLetters(WithPlaceholder, WithPlaceholder);

        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var review = await Single("Review");
        Assert.Contains("заглушки в письме", review.GetProperty("statusReason").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("coverLetter").ValueKind);
        Assert.Equal(0, (await Json(await _api.GetAsync("/api/queue"))).GetArrayLength());

        await Json(await _api.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }));
        Assert.Equal(2, factory.Llm.Calls);
    }

    [Fact]
    public async Task Letter_InventedYearsInWords_GoesToReview_EvenIfVacancyAsksForThem()
    {
        const string invented = "Здравствуйте! У меня более трёх лет работы с C#, буду рад обсудить вашу вакансию.";

        await WriteLetters(invented, invented);

        Assert.Contains("трёх лет", (await Single("Review")).GetProperty("statusReason").GetString());
    }

    [Fact]
    public async Task Questionnaire_Incomplete_IsRetried_ThenAnswered()
    {
        await SubmitQuestionnaire();

        var result = await Answer("""{"answers": ["DIP"]}""", """{"answers": ["DIP", "расскажу на собеседовании"]}""");

        Assert.Equal(1, result.GetProperty("answered").GetInt32());
        Assert.Contains("ровно 2", factory.Llm.Requests[1].Messages[^1].Content);
        Assert.Equal("анкета: проверить ответы LLM", (await Single("Review")).GetProperty("statusReason").GetString());
    }

    [Fact]
    public async Task Questionnaire_IncompleteTwice_GoesToReview_ApproveReturnsToQueue()
    {
        await SubmitQuestionnaire();

        var result = await Answer("""{"answers": ["DIP"]}""", """{"answers": ["DIP"]}""");

        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal("анкета: проверить — ответов 1, а вопросов 2", (await Single("Review")).GetProperty("statusReason").GetString());
        Assert.Empty((await Json(await _api.GetAsync("/api/vacancies?status=Skipped"))).EnumerateArray());

        await Json(await _api.PostAsJsonAsync("/api/vacancies/1/review", new { approve = true }));
        await SubmitQuestionnaire();
        Assert.Equal("Questionnaire", (await Single("Questionnaire")).GetProperty("status").GetString());
    }

    private Task Log(int ok, int rejected, int providerFailures = 0) => factory.WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < ok; i++)
            db.LlmCalls.Add(LlmCall.Succeeded(CoverLetterWriting.Purpose, null, "model", "p", "r", 1, 1, now));
        for (var i = 0; i < rejected; i++)
            db.LlmCalls.Add(LlmCall.Rejected(CoverLetterWriting.Purpose, null, "model", "p", "r", $"{CoverLetterWriting.PlaceholdersError}: [Имя{i}]", 1, 1, now));
        for (var i = 0; i < providerFailures; i++)
            db.LlmCalls.Add(LlmCall.Failed(CoverLetterWriting.Purpose, null, "", "p", "сеть", now));
        return await db.SaveChangesAsync();
    });

    private async Task LinkAdminChat()
    {
        factory.Telegram.Configured = true;
        var code = (await Json(await _api.PostAsync("/api/telegram/link", null))).GetProperty("code").GetString();
        await InScope(sp => sp.GetRequiredService<HandleTelegramMessageCommandHandler>()
            .Handle(new HandleTelegramMessageCommand { ChatId = 42, IsPrivate = true, Text = $"/start {code}", Username = "owner" }));
    }

    private async Task<List<Notification>> RunMonitor()
    {
        await InScope(async sp =>
        {
            await sp.GetRequiredService<LlmQualityMonitor>().NotifyIfFrequentAsync();
            return 0;
        });
        return await factory.WithDbAsync(db => db.Notifications.Where(n => n.Kind == NotificationKind.LlmQuality).ToListAsync());
    }

    [Fact]
    public async Task Monitor_FrequentRejections_OneMessagePerDay()
    {
        await LinkAdminChat();
        await Log(ok: 5, rejected: 5);

        var first = Assert.Single(await RunMonitor());
        Assert.Contains("5 из 10", first.Text);
        Assert.Contains("письма, заглушки в письме: 5", first.Text);

        await Log(ok: 0, rejected: 3);
        Assert.Single(await RunMonitor());
    }

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(30, 5, 0)]
    [InlineData(0, 0, 10)]
    public async Task Monitor_RareRejectionsOrProviderFailures_NoMessage(int ok, int rejected, int providerFailures)
    {
        await LinkAdminChat();
        await Log(ok, rejected, providerFailures);

        Assert.Empty(await RunMonitor());
    }

    [Fact]
    public async Task RejectedLetter_NotifiesOwnerOnReview_WithItsReason()
    {
        await LinkAdminChat();
        await WriteLetters(WithPlaceholder, WithPlaceholder);

        await InScope(sp => sp.GetRequiredService<NotifyReviewsCommandHandler>().Handle(new NotifyReviewsCommand()));

        var notification = Assert.Single(await factory.WithDbAsync(db => db.Notifications.Where(n => n.Kind == NotificationKind.Review).ToListAsync()));
        Assert.StartsWith("review-letter:", notification.Key);
        Assert.Contains("заглушки в письме", notification.Text);
        Assert.Contains("расширение возьмёт свой шаблон", notification.Text);
    }
}