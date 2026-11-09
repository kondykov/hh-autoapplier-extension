using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Commands.Dialogs;
using HhAutoApplier.Application.Commands.Llm;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Vacancies;
using HhAutoApplier.Infrastructure.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Tests.Api;

// Провайдер вернул вариант без текста (#81). Настоящий клиент поверх заглушки HTTP: FakeLlm не воспроизводит разбор ответа провайдера
[Collection(ApiCollection.Name)]
public class LlmEmptyResponseTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Model = "reasoning-model";

    private const string Empty = $$$"""
        {"model":"{{{Model}}}","choices":[{"message":{"role":"assistant","content":null},"finish_reason":"length"}],"usage":{"prompt_tokens":100,"completion_tokens":800}}
        """;

    private const string Letter = "Здравствуйте! Заинтересовала ваша вакансия, есть опыт backend-разработки на C#.";

    // Доля по умолчанию — LlmOptions.UserDailyRequestLimit
    private const int Share = 150;

    private readonly HttpClient _api = factory.CreateApiClient();

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await Json(await _api.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#" }));
        await Json(await _api.PutAsJsonAsync("/api/resumes", new { name = ".NET", match = "C#", keywords = new[] { "c#" }, summary = "Backend-разработчик: C#." }));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Answer(string content) => JsonSerializer.Serialize(new
    {
        model = Model,
        choices = new[] { new { message = new { role = "assistant", content } } },
        usage = new { prompt_tokens = 10, completion_tokens = 5 },
    });

    // Отвечает по очереди; лишний запрос роняет тест
    private sealed class Provider(params string[] bodies) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bodies[Requests++]) });
    }

    // Обработчик из контейнера, только LLM — настоящий клиент
    private async Task<TResult> Run<THandler, TResult>(Provider provider, Func<THandler, Task<TResult>> action) where THandler : class
    {
        var client = new OpenAiCompatibleClient(new HttpClient(provider), Options.Create(new LlmOptions { ApiKey = "secret", MaxRetries = 0 }),
            new LlmBudget(TimeProvider.System), NullLogger<OpenAiCompatibleClient>.Instance);
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider, client));
    }

    private Task<WriteCoverLettersResult> WriteLetters(Provider provider) =>
        Run<WriteCoverLettersCommandHandler, WriteCoverLettersResult>(provider, h => h.Handle(new WriteCoverLettersCommand { Limit = 10 }));

    private async Task Import(params string[] ids)
    {
        await Json(await _api.PostAsJsonAsync("/api/import", new
        {
            search = "dotnet",
            vacancies = ids.Select(id => new { id, title = "C# разработчик", company = "Acme", description = "Нужен C#", salaryCurrency = "RUR", workFormat = "Удалённо" }),
        }));
        await factory.ConfirmImportedAsync();
    }

    private Task<List<LlmCall>> Calls(string purpose) =>
        factory.WithDbAsync(db => db.LlmCalls.AsNoTracking().Where(c => c.Purpose == purpose).OrderBy(c => c.Id).ToListAsync());

    private Task<List<VacancyStatus>> Statuses() =>
        factory.WithDbAsync(db => db.JobApplications.AsNoTracking().OrderBy(a => a.Status).Select(a => a.Status).ToListAsync());

    private static void AssertEmptyAnswerLogged(LlmCall call)
    {
        Assert.Equal(Model, call.Model);
        Assert.Equal(LlmCall.Unparsed, call.Error);
        Assert.Equal("", call.Response);
        Assert.Equal((100, 800), (call.PromptTokens, call.CompletionTokens));
    }

    [Fact]
    public async Task Letter_EmptyTwice_IsUnparsed_AndNotRequestedForADay()
    {
        await Import("1");

        foreach (var _ in new[] { 1, 2 })
        {
            var dto = (await WriteLetters(new Provider(Empty))).AsT0;
            Assert.Equal((0, 1), (dto.Written, dto.Failed));
            Assert.Null(dto.StoppedBy);
        }

        var calls = await Calls(CoverLetterWriting.Purpose);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, AssertEmptyAnswerLogged);

        var idle = new Provider();
        var third = (await WriteLetters(idle)).AsT0;
        Assert.Equal(0, idle.Requests);
        Assert.Equal((0, 0), (third.Written, third.Failed));
    }

    [Fact]
    public async Task Letter_Empty_PassContinuesWithNext()
    {
        await Import("1", "2");
        var provider = new Provider(Empty, Answer(Letter));

        var dto = (await WriteLetters(provider)).AsT0;

        Assert.Equal(2, provider.Requests);
        Assert.Equal((1, 1), (dto.Written, dto.Failed));
        Assert.Null(dto.StoppedBy);
    }

    // Модель ответила: запрос тратит долю пользователя, как любой другой ответ
    [Fact]
    public async Task EmptyAnswer_CountsTowardsUserShare()
    {
        await Import("1");
        var admin = await factory.WithDbAsync(async db => (await db.Users.SingleAsync(u => u.Email == ApiFactory.AdminEmail)).Id);
        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < Share - 1; i++)
                db.LlmCalls.Add(LlmCall.Succeeded("test", null, "model", "p", "r", 1, 1, DateTimeOffset.UtcNow).For(admin));
            return await db.SaveChangesAsync();
        });

        await WriteLetters(new Provider(Empty));

        await using var scope = factory.Services.CreateAsyncScope();
        var usage = await scope.ServiceProvider.GetRequiredService<LlmUserQuota>().LoadAsync(default);
        Assert.True(usage.IsExhausted(admin));
    }

    [Fact]
    public async Task Evaluation_Empty_IsDoubtful_PassContinuesWithNext()
    {
        factory.Llm.Configured = true;
        await Import("1", "2");
        var provider = new Provider(Empty, Answer("""{"verdict": "fit", "score": 80, "resume": null, "reason": "подходит"}"""));

        var dto = (await Run<EvaluatePendingVacanciesCommandHandler, EvaluatePendingResult>(provider, h => h.Handle(new EvaluatePendingVacanciesCommand { Limit = 10 }))).AsT0;

        Assert.Equal(2, provider.Requests);
        Assert.Equal((1, 1, 0), (dto.Fit, dto.Doubtful, dto.Remaining));
        Assert.Null(dto.StoppedBy);
        AssertEmptyAnswerLogged((await Calls(VacancyAssessment.Purpose))[0]);
        Assert.Equal([VacancyStatus.Queued, VacancyStatus.Review], (await Statuses()).Order());
    }

    [Fact]
    public async Task Questionnaire_Empty_IsSkipped_PassContinuesWithNext()
    {
        await Import("1", "2");
        foreach (var id in new[] { "1", "2" })
            await Json(await _api.PostAsJsonAsync($"/api/vacancies/{id}/questionnaire", new { questions = new object[] { new { question = "Какой принцип?", type = "Radio", options = new[] { "SRP", "DIP" } } } }));
        var provider = new Provider(Empty, Answer("""{"answers": ["DIP"]}"""));

        var dto = (await Run<AnswerQuestionnairesCommandHandler, AnswerQuestionnairesResult>(provider, h => h.Handle(new AnswerQuestionnairesCommand { Limit = 10 }))).AsT0;

        Assert.Equal(2, provider.Requests);
        Assert.Equal((1, 1, 0), (dto.Answered, dto.Failed, dto.Remaining));
        Assert.Null(dto.StoppedBy);
        AssertEmptyAnswerLogged((await Calls(QuestionnaireAnswering.Purpose))[0]);
        Assert.Equal([VacancyStatus.Queued, VacancyStatus.Skipped], (await Statuses()).Order());
    }

    [Fact]
    public async Task Dialog_Empty_IsOther_PassContinuesWithNext()
    {
        await Import("1", "2");
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        object Chat(long id, string vacancyId, DateTimeOffset received) =>
            new { chatId = id, vacancyId, title = "C#", company = "Acme", state = "RESPONSE", messageId = id, text = "Когда удобно созвониться?", at = received, fromEmployer = true };
        await Json(await _api.PostAsJsonAsync("/api/dialogs", new { chats = new[] { Chat(1, "1", at), Chat(2, "2", at.AddMinutes(1)) } }));
        var provider = new Provider(Empty, Answer("""{"kind": "question", "summary": "про созвон", "reply": null}"""));

        var dto = (await Run<AnalyzeDialogsCommandHandler, AnalyzeDialogsResult>(provider, h => h.Handle(new AnalyzeDialogsCommand { Limit = 10 }))).AsT0;

        Assert.Equal(2, provider.Requests);
        Assert.Equal((1, 1, 0), (dto.Analyzed, dto.Failed, dto.Remaining));
        Assert.Null(dto.StoppedBy);
        AssertEmptyAnswerLogged((await Calls(DialogAnalysis.Purpose))[0]);
    }

    // Проверка ключа: успех без ответа был бы ложным, API отвечает ошибкой, а журнал — с моделью
    [Fact]
    public async Task Check_Empty_IsError_LoggedWithModel()
    {
        var provider = new Provider(Empty);

        var result = await Run<CheckLlmCommandHandler, CheckLlmResult>(provider, h => h.Handle(new CheckLlmCommand()));

        Assert.True(result.IsT1);
        Assert.Contains("нет текста", result.AsT1.Message);
        AssertEmptyAnswerLogged(Assert.Single(await Calls("check")));
    }

    // Нет ни одного варианта: это сбой провайдера, проход останавливается, письмо не попадает в суточный фильтр
    [Fact]
    public async Task NoChoices_StopsPass_AndDoesNotBlockLetter()
    {
        await Import("1", "2");
        var provider = new Provider("""{"choices":[]}""");

        var dto = (await WriteLetters(provider)).AsT0;

        Assert.Equal(1, provider.Requests);
        Assert.Contains("нет текста", dto.StoppedBy);
        var call = Assert.Single(await Calls(CoverLetterWriting.Purpose));
        Assert.Equal("", call.Model);
        Assert.NotEqual(LlmCall.Unparsed, call.Error);
    }
}
