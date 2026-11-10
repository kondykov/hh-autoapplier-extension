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

    // #80: неразобранный ответ не запрашивается на каждом проходе конвейера
    [Fact]
    public async Task Letter_UnparsedTwice_IsNotRequestedForADay()
    {
        await WriteLetters("не письмо");
        await WriteLetters("{}");
        Assert.Equal(2, factory.Llm.Calls);

        var result = await WriteLetters(Good);

        Assert.Equal(2, factory.Llm.Calls);
        Assert.Equal(0, result.GetProperty("written").GetInt32());
        Assert.Single(factory.Llm.Answers);
        Assert.Equal(JsonValueKind.Null, (await Single("Queued")).GetProperty("coverLetter").ValueKind);
    }

    // Один неразобранный ответ — ещё не повод отказаться от письма
    [Fact]
    public async Task Letter_UnparsedOnce_IsRequestedAgain()
    {
        await WriteLetters("не письмо");

        Assert.Equal(1, (await WriteLetters(Good)).GetProperty("written").GetInt32());
    }

    // Сбой провайдера: ответа не было, письмо пишется после восстановления
    [Fact]
    public async Task Letter_ProviderFailures_DoNotBlock()
    {
        for (var i = 0; i < 3; i++)
            Assert.Equal(JsonValueKind.String, (await WriteLetters()).GetProperty("stoppedBy").ValueKind);

        Assert.Equal(3, await factory.WithDbAsync(db => db.LlmCalls.CountAsync(c => c.Model == "" && c.Error != null)));
        Assert.Equal(1, (await WriteLetters(Good)).GetProperty("written").GetInt32());
    }

    // Обещания и чужие технологии (#82): один повтор с замечаниями, затем ручная проверка без письма, сутки не переписывается
    [Theory]
    [InlineData("Здравствуйте! Есть опыт на C#, готов выполнить тестовое задание в удобное время.", CoverLetterWriting.PromisesError, "тестовому заданию")]
    [InlineData("Здравствуйте! Есть опыт на C#, уверенно владею Docker и Kafka в продакшене.", CoverLetterWriting.TechnologiesError, "Docker, Kafka")]
    public async Task Letter_PromiseOrForeignTechnology_RetriedThenReview(string bad, string error, string remark)
    {
        var result = await WriteLetters(bad, bad);

        Assert.Equal((0, 1), (result.GetProperty("written").GetInt32(), result.GetProperty("failed").GetInt32()));
        var retry = factory.Llm.Requests[1].Messages;
        Assert.Equal("assistant", retry[^2].Role);
        Assert.Contains(remark, retry[^1].Content);

        var review = await Single("Review");
        Assert.Contains(error, review.GetProperty("statusReason").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("coverLetter").ValueKind);

        var errors = await factory.WithDbAsync(db => db.LlmCalls.Where(c => c.Error != null).Select(c => c.Error!).ToListAsync());
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.StartsWith(error, e));

        // Следующий проход конвейера: письмо ждёт сутки, запросов к LLM нет
        var calls = factory.Llm.Calls;
        await WriteLetters();
        Assert.Equal(calls, factory.Llm.Calls);
    }

    [Fact]
    public async Task Letter_Promise_FixedOnRetry_IsWritten()
    {
        var result = await WriteLetters("Здравствуйте! Есть опыт на C#, хочу обсудить желаемую зарплату на встрече.", Good);

        Assert.Equal(1, result.GetProperty("written").GetInt32());
        Assert.Equal(Good, (await Single("Queued")).GetProperty("coverLetter").GetString());
    }

    [Theory]
    [InlineData(CoverLetterWriting.PromisesError)]
    [InlineData(CoverLetterWriting.TechnologiesError)]
    public async Task Letter_RejectedTwiceForNewReason_NotRequestedForADay(string error)
    {
        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < 2; i++)
                db.LlmCalls.Add(LlmCall.Rejected(CoverLetterWriting.Purpose, "1", "fake-model", "промпт", "письмо", $"{error}: детали", 100, 20, DateTimeOffset.UtcNow).For(await AdminId()));
            return await db.SaveChangesAsync();
        });

        var calls = factory.Llm.Calls;
        var result = await WriteLetters();

        Assert.Equal(calls, factory.Llm.Calls);
        Assert.Equal(0, result.GetProperty("written").GetInt32());
    }

    private Task<Guid?> AdminId() => factory.WithDbAsync(db => db.JobApplications.Select(a => a.UserId).FirstAsync());

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