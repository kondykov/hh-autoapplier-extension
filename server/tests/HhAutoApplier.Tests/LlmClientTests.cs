using System.Net;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Tests;

public class LlmClientTests
{
    private const string Ok = """
        {"model":"llama-3.3-70b-versatile","choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":12,"completion_tokens":1}}
        """;

    // Отвечает по очереди заданными ответами и запоминает запросы
    private sealed class Script(params (HttpStatusCode Status, string Body, TimeSpan? RetryAfter)[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            var (status, body, retryAfter) = responses[Math.Min(_next++, responses.Length - 1)];
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            if (retryAfter is not null)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            return response;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (OpenAiCompatibleClient Client, Script Http, List<TimeSpan> Pauses) Create(
        Script http, string? apiKey = "secret", int dailyLimit = 300, LlmBudget? budget = null)
    {
        var pauses = new List<TimeSpan>();
        var options = Options.Create(new LlmOptions { ApiKey = apiKey, MaxRetries = 2, DailyRequestLimit = dailyLimit });
        var client = new OpenAiCompatibleClient(
            new HttpClient(http), options, budget ?? new LlmBudget(TimeProvider.System), NullLogger<OpenAiCompatibleClient>.Instance,
            (pause, _) =>
            {
                pauses.Add(pause);
                return Task.CompletedTask;
            });
        return (client, http, pauses);
    }

    private static readonly LlmRequest Ask = new([LlmMessage.System("Ты помощник"), LlmMessage.User("Привет")], Temperature: 0.1, MaxTokens: 50, Json: true);

    [Fact]
    public async Task Sends_OpenAiChatRequest_AndReadsAnswer()
    {
        var (client, http, _) = Create(new Script((HttpStatusCode.OK, Ok, null)));

        var result = await client.CompleteAsync(Ask, default);

        Assert.Equal(new LlmResult("ok", "llama-3.3-70b-versatile", 12, 1), result);
        var (request, body) = Assert.Single(http.Requests);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString());

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("llama-3.3-70b-versatile", json.GetProperty("model").GetString());
        Assert.Equal(["system", "user"], json.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal(50, json.GetProperty("max_tokens").GetInt32());
        Assert.Equal("json_object", json.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Retries_On429And5xx_UsingRetryAfter()
    {
        var (client, http, pauses) = Create(new Script(
            (HttpStatusCode.TooManyRequests, "rate limit", TimeSpan.FromSeconds(7)),
            (HttpStatusCode.BadGateway, "oops", null),
            (HttpStatusCode.OK, Ok, null)));

        Assert.Equal("ok", (await client.CompleteAsync(Ask, default)).Content);
        Assert.Equal(3, http.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2)], pauses);
    }

    [Fact]
    public async Task GivesUp_AfterRetries_AndOnClientErrors()
    {
        var (exhausted, http, _) = Create(new Script((HttpStatusCode.ServiceUnavailable, "down", null)));
        var e = await Assert.ThrowsAsync<LlmUnavailableException>(() => exhausted.CompleteAsync(Ask, default));
        Assert.Contains("503", e.Message);
        Assert.Equal(3, http.Requests.Count); // попытка + 2 повтора

        var (badKey, keyHttp, _) = Create(new Script((HttpStatusCode.Unauthorized, "invalid api key", null)));
        var unauthorized = await Assert.ThrowsAsync<LlmUnavailableException>(() => badKey.CompleteAsync(Ask, default));
        Assert.Contains("401", unauthorized.Message);
        Assert.Single(keyHttp.Requests); // 4xx не повторяем
    }

    [Fact]
    public async Task WithoutKey_NotConfigured_AndNoRequest()
    {
        var (client, http, _) = Create(new Script((HttpStatusCode.OK, Ok, null)), apiKey: "");

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Ask, default));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task DailyLimit_StopsRequests_UntilNextUtcDay()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero));
        var budget = new LlmBudget(clock);
        var (client, http, _) = Create(new Script((HttpStatusCode.OK, Ok, null)), dailyLimit: 2, budget: budget);

        await client.CompleteAsync(Ask, default);
        await client.CompleteAsync(Ask, default);
        var e = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Ask, default));
        Assert.Contains("дневной лимит", e.Message);
        Assert.Equal(2, http.Requests.Count);

        clock.Now = clock.Now.AddHours(2);
        await client.CompleteAsync(Ask, default);
        Assert.Equal(1, budget.Used);
    }
}
