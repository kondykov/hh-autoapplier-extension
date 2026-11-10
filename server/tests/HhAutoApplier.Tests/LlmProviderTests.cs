using System.Net;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Infrastructure;
using HhAutoApplier.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Tests;

// Профили провайдеров и вход по токену (#69): всё, что не зависит от конкретного провайдера
public class LlmProviderTests
{
    private const string Answer = """
        {"model":"m","choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
        """;

    private static readonly LlmRequest Ask = new([LlmMessage.User("Привет")], Temperature: 0, MaxTokens: 50, Json: true);

    private sealed class Script(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _next;

        public List<(string? Authorization, string Body)> Requests { get; } = [];

        public string ErrorBody { get; init; } = "ошибка";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(ct)));
            var status = statuses[Math.Min(_next++, statuses.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? Answer : ErrorBody) };
        }
    }

    private sealed class FakeTokens : ILlmTokenSource
    {
        private int _issued;

        public List<string> Invalidated { get; } = [];

        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult($"token-{_issued + 1}");

        public void Invalidate(string token)
        {
            Invalidated.Add(token);
            _issued++;
        }
    }

    private static OpenAiCompatibleClient Create(Script http, LlmOptions options, ILlmTokenSource? tokens = null) =>
        new(new HttpClient(http), Options.Create(options), new LlmBudget(TimeProvider.System), NullLogger<OpenAiCompatibleClient>.Instance,
            (_, _) => Task.CompletedTask, tokens);

    [Fact]
    public async Task Default_IsOpenAiProfile_KeyAsBearer_JsonObject_TemperatureUntouched()
    {
        var http = new Script(HttpStatusCode.OK);

        await Create(http, new LlmOptions { ApiKey = "groq-key" }).CompleteAsync(Ask, default);

        var (authorization, body) = Assert.Single(http.Requests);
        Assert.Equal("Bearer groq-key", authorization);
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("json_object", json.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(0, json.GetProperty("temperature").GetDouble());
    }

    [Theory]
    [InlineData(LlmOptions.NoJsonMode, false)]
    [InlineData("NONE", false)]
    [InlineData(LlmOptions.JsonObjectMode, true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public async Task JsonMode_OverridesProfile(string? mode, bool expected)
    {
        var http = new Script(HttpStatusCode.OK);

        await Create(http, new LlmOptions { ApiKey = "k", JsonMode = mode }).CompleteAsync(Ask, default);

        Assert.Equal(expected, JsonDocument.Parse(Assert.Single(http.Requests).Body).RootElement.TryGetProperty("response_format", out _));
    }

    [Theory]
    [InlineData(0, 0.01, 0.01)]
    [InlineData(0.2, 0.01, 0.2)]
    [InlineData(0, 0, 0)]
    public void Profile_RaisesTemperatureToMinimum(double requested, double minimum, double expected) =>
        Assert.Equal(expected, new LlmProviderProfile("x", MinTemperature: minimum).Temperature(requested));

    [Fact]
    public async Task TokenSource_SendsToken_NotKey()
    {
        var http = new Script(HttpStatusCode.OK);

        await Create(http, new LlmOptions { ApiKey = "secret" }, new FakeTokens()).CompleteAsync(Ask, default);

        Assert.Equal("Bearer token-1", Assert.Single(http.Requests).Authorization);
    }

    [Fact]
    public async Task TokenSource_On401_RefreshesOnce_AndRepeats()
    {
        var http = new Script(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var tokens = new FakeTokens();

        Assert.Equal("ok", (await Create(http, new LlmOptions { ApiKey = "k" }, tokens).CompleteAsync(Ask, default)).Content);

        Assert.Equal(["token-1"], tokens.Invalidated);
        Assert.Equal(["Bearer token-1", "Bearer token-2"], http.Requests.Select(r => r.Authorization));
    }

    [Fact]
    public async Task TokenSource_On401Twice_GivesUp_WithoutEchoingResponse()
    {
        var http = new Script(HttpStatusCode.Unauthorized) { ErrorBody = "bad token-1" };

        var e = await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Create(http, new LlmOptions { ApiKey = "k" }, new FakeTokens()).CompleteAsync(Ask, default));

        Assert.Contains("401", e.Message);
        Assert.DoesNotContain("token-", e.Message);
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("groq")]
    [InlineData("openai2")]
    public void UnknownProvider_FailsAtStart(string provider)
    {
        var e = Assert.Throws<InvalidOperationException>(() => new LlmOptions { Provider = provider }.EnsureValid());
        Assert.Contains(provider, e.Message);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("OpenAI")]
    [InlineData("")]
    public void KnownProviders_Pass(string provider) => new LlmOptions { Provider = provider }.EnsureValid();

    [Fact]
    public void UnknownJsonMode_FailsAtStart() =>
        Assert.Throws<InvalidOperationException>(() => new LlmOptions { JsonMode = "json" }.EnsureValid());

    private static readonly Dictionary<string, LlmProviderProfile> WithOwnAddress = new(StringComparer.OrdinalIgnoreCase)
    {
        ["other"] = new LlmProviderProfile("other", NeedsOwnBaseUrl: true),
    };

    [Theory]
    [InlineData(LlmOptions.DefaultBaseUrl)]
    [InlineData(LlmOptions.DefaultBaseUrl + "/")]
    public void ProviderWithOwnAddress_OnDefaultGroqAddress_FailsAtStart(string url)
    {
        var e = Assert.Throws<InvalidOperationException>(() => new LlmOptions { Provider = "other", BaseUrl = url }.EnsureValid(WithOwnAddress));
        Assert.Contains("LLM_BASE_URL", e.Message);
    }

    [Fact]
    public void ProviderWithOwnAddress_AddedByDataOnly_Passes() =>
        new LlmOptions { Provider = "OTHER", BaseUrl = "https://other.example.test/v1" }.EnsureValid(WithOwnAddress);

    [Fact]
    public async Task Registration_OpenAi_NoTokenSource_KeyGoesDirectly()
    {
        var http = new Script(HttpStatusCode.OK);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
            ["Hh:Enabled"] = "false",
            ["Llm:ApiKey"] = "groq-key",
            ["Llm:BaseUrl"] = "https://api.example.test/v1",
        }).Build());
        services.AddSingleton(new LlmBudget(TimeProvider.System));
        services.AddHttpClient(nameof(ILlmClient)).ConfigurePrimaryHttpMessageHandler(() => http);
        await using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetService<ILlmTokenSource>());
        await sp.GetRequiredService<ILlmClient>().CompleteAsync(Ask, default);

        Assert.Equal("Bearer groq-key", Assert.Single(http.Requests).Authorization);
    }
}