using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Infrastructure;
using HhAutoApplier.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Tests;

// ГигаЧат здесь — подставной HTTP-обработчик: живой сервис в тестах не вызывается, ключи вымышленные
public class GigaChatClientTests
{
    private const string Key = "dGVzdC1jbGllbnQ6dGVzdC1zZWNyZXQ=";

    private const string ChatUrl = "https://gigachat.example.test/api/v1/chat/completions";

    private const string AuthUrl = "https://ngw.example.test:9443/api/v2/oauth";

    private const string Answer = """
        {"model":"GigaChat:2.0.28.2","choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":12,"completion_tokens":1}}
        """;

    private static readonly DateTimeOffset Start = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly LlmRequest Ask = new([LlmMessage.System("Ты помощник"), LlmMessage.User("Привет")], Temperature: 0.1, MaxTokens: 50, Json: true);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Sent(HttpRequestMessage Request, string Body, string? Authorization);

    // Выдаёт токены token-1, token-2, … и отвечает на чат заданными кодами (последний повторяется)
    private sealed class FakeGigaChat(DateTimeOffset expiresAt, params HttpStatusCode[] chat) : HttpMessageHandler
    {
        private int _exchanges;

        private int _chats;

        public List<Sent> OAuth { get; } = [];

        public List<Sent> Chat { get; } = [];

        public string ChatErrorBody { get; init; } = "ошибка провайдера";

        public HttpStatusCode OAuthStatus { get; init; } = HttpStatusCode.OK;

        public string? OAuthBody { get; init; }

        public TaskCompletionSource? OAuthGate { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sent = new Sent(request, await request.Content!.ReadAsStringAsync(ct), request.Headers.Authorization?.ToString());
            if (request.RequestUri!.Host.StartsWith("ngw", StringComparison.Ordinal))
            {
                lock (OAuth)
                    OAuth.Add(sent);

                if (OAuthGate is not null)
                    await OAuthGate.Task.WaitAsync(ct);

                var token = $"token-{Interlocked.Increment(ref _exchanges)}";
                return new HttpResponseMessage(OAuthStatus)
                {
                    Content = new StringContent(OAuthBody ?? $$"""{"access_token":"{{token}}","expires_at":{{expiresAt.ToUnixTimeMilliseconds()}}}"""),
                };
            }

            lock (Chat)
                Chat.Add(sent);

            var status = chat[Math.Min(Interlocked.Increment(ref _chats) - 1, chat.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? Answer : ChatErrorBody) };
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private static LlmOptions Options_() => new()
    {
        Provider = LlmOptions.GigaChatProvider,
        ApiKey = Key,
        BaseUrl = "https://gigachat.example.test/api/v1",
        Model = "GigaChat",
        AuthUrl = AuthUrl,
        MaxRetries = 2,
    };

    private static (OpenAiCompatibleClient Client, RecordingLogger<OpenAiCompatibleClient> Log) Create(FakeGigaChat http, TimeProvider clock, LlmOptions? options = null)
    {
        var opt = Options.Create(options ?? Options_());
        var log = new RecordingLogger<OpenAiCompatibleClient>();
        var tokens = new GigaChatTokenSource(new FakeFactory(http), opt, clock);
        var client = new OpenAiCompatibleClient(new HttpClient(http, disposeHandler: false), opt, new LlmBudget(clock), log, (_, _) => Task.CompletedTask, tokens);
        return (client, log);
    }

    [Fact]
    public async Task ExchangesKey_ForToken_AndSendsTokenAsBearer()
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK);
        var (client, _) = Create(http, new FixedClock(Start));

        var result = await client.CompleteAsync(Ask, default);

        Assert.Equal(new LlmResult("ok", "GigaChat:2.0.28.2", 12, 1), result);

        var oauth = Assert.Single(http.OAuth);
        Assert.Equal(AuthUrl, oauth.Request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, oauth.Request.Method);
        Assert.Equal($"Basic {Key}", oauth.Authorization);
        Assert.True(Guid.TryParse(oauth.Request.Headers.GetValues("RqUID").Single(), out _));
        Assert.Equal("application/x-www-form-urlencoded", oauth.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("scope=GIGACHAT_API_PERS", oauth.Body);

        var chat = Assert.Single(http.Chat);
        Assert.Equal(ChatUrl, chat.Request.RequestUri!.ToString());
        Assert.Equal("Bearer token-1", chat.Authorization);
    }

    [Fact]
    public async Task UsesConfiguredScope()
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK);
        var options = Options_();
        options.Scope = "GIGACHAT_API_B2B";
        var (client, _) = Create(http, new FixedClock(Start), options);

        await client.CompleteAsync(Ask, default);

        Assert.Equal("scope=GIGACHAT_API_B2B", Assert.Single(http.OAuth).Body);
    }

    [Fact]
    public async Task DoesNotSendJsonMode_AndKeepsTemperaturePositive()
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK);
        var (client, _) = Create(http, new FixedClock(Start));

        await client.CompleteAsync(Ask with { Temperature = 0 }, default);

        var json = JsonDocument.Parse(Assert.Single(http.Chat).Body).RootElement;
        Assert.False(json.TryGetProperty("response_format", out _));
        Assert.True(json.GetProperty("temperature").GetDouble() > 0);
        Assert.Equal("GigaChat", json.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CachesToken_UntilExpiryMinusMargin()
    {
        var clock = new FixedClock(Start);
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK);
        var (client, _) = Create(http, clock);

        await client.CompleteAsync(Ask, default);
        clock.Now = Start.AddMinutes(27);
        await client.CompleteAsync(Ask, default);
        Assert.Single(http.OAuth);
        Assert.Equal("Bearer token-1", http.Chat[^1].Authorization);

        clock.Now = Start.AddMinutes(29);
        await client.CompleteAsync(Ask, default);
        Assert.Equal(2, http.OAuth.Count);
        Assert.Equal("Bearer token-2", http.Chat[^1].Authorization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsExpiresAt_InMillisecondsOrSeconds(bool seconds)
    {
        var expires = Start.AddMinutes(30);
        var value = seconds ? expires.ToUnixTimeSeconds() : expires.ToUnixTimeMilliseconds();
        var clock = new FixedClock(Start);
        var http = new FakeGigaChat(expires, HttpStatusCode.OK) { OAuthBody = $$"""{"access_token":"same","expires_at":{{value}}}""" };
        var (client, _) = Create(http, clock);

        await client.CompleteAsync(Ask, default);
        clock.Now = Start.AddMinutes(27);
        await client.CompleteAsync(Ask, default);

        Assert.Single(http.OAuth);
    }

    [Fact]
    public async Task ParallelRequests_ShareOneExchange()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK) { OAuthGate = gate };
        var opt = Options.Create(Options_());
        var tokens = new GigaChatTokenSource(new FakeFactory(http), opt, new FixedClock(Start));

        var waiting = Enumerable.Range(0, 8).Select(_ => Task.Run(() => tokens.GetAsync(default))).ToList();
        await Task.Delay(100);
        gate.SetResult();
        var results = await Task.WhenAll(waiting);

        Assert.Single(http.OAuth);
        Assert.All(results, t => Assert.Equal("token-1", t));
    }

    [Fact]
    public async Task On401_ExchangesOnce_AndRepeats()
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var (client, _) = Create(http, new FixedClock(Start));

        Assert.Equal("ok", (await client.CompleteAsync(Ask, default)).Content);

        Assert.Equal(2, http.OAuth.Count);
        Assert.Equal(["Bearer token-1", "Bearer token-2"], http.Chat.Select(c => c.Authorization));
    }

    [Fact]
    public async Task On401Twice_GivesUp_WithoutEchoingResponse()
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.Unauthorized) { ChatErrorBody = "bad token-1 and token-2" };
        var (client, _) = Create(http, new FixedClock(Start));

        var e = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Ask, default));

        Assert.Contains("401", e.Message);
        Assert.DoesNotContain("token-", e.Message);
        Assert.Equal(2, http.OAuth.Count);
        Assert.Equal(2, http.Chat.Count);
    }

    [Fact]
    public async Task Invalidate_IgnoresReplacedToken()
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK);
        var tokens = new GigaChatTokenSource(new FakeFactory(http), Options.Create(Options_()), new FixedClock(Start));

        var first = await tokens.GetAsync(default);
        tokens.Invalidate(first);
        var second = await tokens.GetAsync(default);
        tokens.Invalidate(first);

        Assert.Equal(second, await tokens.GetAsync(default));
        Assert.Equal(2, http.OAuth.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "401")]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    public async Task FailedExchange_HidesKeyAndBody_AndSkipsChat(HttpStatusCode status, string code)
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK) { OAuthStatus = status, OAuthBody = $"сервер вернул {Key}" };
        var (client, log) = Create(http, new FixedClock(Start));

        var e = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Ask, default));

        Assert.Contains(code, e.Message);
        Assert.DoesNotContain(Key, e.Message);
        Assert.Empty(http.Chat);
        Assert.DoesNotContain(log.Lines, l => l.Contains(Key));
    }

    [Theory]
    [InlineData("""{"expires_at":1}""")]
    [InlineData("не json")]
    public async Task BadTokenResponse_IsUnavailable(string body)
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.OK) { OAuthBody = body };
        var (client, _) = Create(http, new FixedClock(Start));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Ask, default));
        Assert.Empty(http.Chat);
    }

    [Fact]
    public async Task Retries_DoNotLogKeyOrToken()
    {
        var http = new FakeGigaChat(Start.AddMinutes(30), HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var (client, log) = Create(http, new FixedClock(Start));

        await client.CompleteAsync(Ask, default);

        Assert.NotEmpty(log.Lines);
        Assert.DoesNotContain(log.Lines, l => l.Contains(Key) || l.Contains("token-"));
    }

    [Theory]
    [InlineData("groq")]
    [InlineData("gigachat2")]
    public void UnknownProvider_FailsAtStart(string provider)
    {
        var e = Assert.Throws<InvalidOperationException>(() => new LlmOptions { Provider = provider }.EnsureValid());
        Assert.Contains(provider, e.Message);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("GigaChat")]
    [InlineData("")]
    public void KnownProviders_Pass(string provider) =>
        new LlmOptions { Provider = provider, BaseUrl = "https://gigachat.example.test/api/v1" }.EnsureValid();

    [Fact]
    public void GigaChat_WithDefaultGroqAddress_FailsAtStart() =>
        Assert.Throws<InvalidOperationException>(() => new LlmOptions { Provider = LlmOptions.GigaChatProvider }.EnsureValid());

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings, FakeGigaChat http)
    {
        settings["ConnectionStrings:Default"] = "Host=localhost;Database=unused";
        settings["Hh:Enabled"] = "false";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton(new LlmBudget(TimeProvider.System));
        services.AddHttpClient(nameof(ILlmClient)).ConfigurePrimaryHttpMessageHandler(() => http);
        services.AddHttpClient(GigaChatTokenSource.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => http);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Registration_GigaChat_UsesTokenSource()
    {
        var http = new FakeGigaChat(DateTimeOffset.UtcNow.AddMinutes(30), HttpStatusCode.OK);
        await using var sp = BuildServices(new()
        {
            ["Llm:Provider"] = "gigachat",
            ["Llm:ApiKey"] = Key,
            ["Llm:BaseUrl"] = "https://gigachat.example.test/api/v1",
            ["Llm:AuthUrl"] = AuthUrl,
            ["Llm:Model"] = "GigaChat",
        }, http);

        Assert.NotNull(sp.GetService<ILlmTokenSource>());
        await sp.GetRequiredService<ILlmClient>().CompleteAsync(Ask, default);

        Assert.Single(http.OAuth);
        Assert.Equal("Bearer token-1", Assert.Single(http.Chat).Authorization);
    }

    [Fact]
    public async Task Registration_OpenAi_SendsKeyDirectly_WithJsonMode()
    {
        var http = new FakeGigaChat(DateTimeOffset.UtcNow.AddMinutes(30), HttpStatusCode.OK);
        await using var sp = BuildServices(new() { ["Llm:ApiKey"] = "groq-key", ["Llm:BaseUrl"] = "https://api.example.test/v1" }, http);

        Assert.Null(sp.GetService<ILlmTokenSource>());
        await sp.GetRequiredService<ILlmClient>().CompleteAsync(Ask, default);

        Assert.Empty(http.OAuth);
        var chat = Assert.Single(http.Chat);
        Assert.Equal("Bearer groq-key", chat.Authorization);
        Assert.Equal("json_object", JsonDocument.Parse(chat.Body).RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public void Root_IsPinnedRussianTrustedRootCa()
    {
        using var root = GigaChatTls.LoadRoot();

        Assert.Equal(GigaChatTls.RootSha256, Convert.ToHexString(SHA256.HashData(root.RawData)));
        Assert.Contains("CN=Russian Trusted Root CA", root.Subject);
        Assert.Equal(root.Subject, root.Issuer);
    }

    private static X509Certificate2 CreateCa(string name, X509Certificate2? issuer = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        // Сертификат не может жить дольше выпустившего
        var to = DateTimeOffset.UtcNow.AddDays(issuer is null ? 30 : 20);
        if (issuer is null)
            return request.CreateSelfSigned(from, to);

        using var signed = request.Create(issuer, from, to, RandomNumberGenerator.GetBytes(8));
        return signed.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 CreateLeaf(X509Certificate2 issuer)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=ngw.example.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        return request.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), RandomNumberGenerator.GetBytes(8));
    }

    private static X509Chain Presented(params X509Certificate2[] intermediates)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.ExtraStore.AddRange(intermediates);
        return chain;
    }

    [Fact]
    public void Tls_AcceptsChainToPinnedRoot_ThroughPresentedIntermediate()
    {
        using var root = CreateCa("Test Root");
        using var sub = CreateCa("Test Sub", root);
        using var leaf = CreateLeaf(sub);
        using var presented = Presented(sub);

        Assert.True(GigaChatTls.Validate(root, leaf, presented, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Tls_RejectsForeignRoot()
    {
        using var pinned = CreateCa("Pinned Root");
        using var foreign = CreateCa("Foreign Root");
        using var sub = CreateCa("Foreign Sub", foreign);
        using var leaf = CreateLeaf(sub);
        using var presented = Presented(sub, foreign);

        Assert.False(GigaChatTls.Validate(pinned, leaf, presented, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void Tls_RejectsOtherErrors_EvenWithPinnedRoot(SslPolicyErrors errors)
    {
        using var root = CreateCa("Test Root");
        using var leaf = CreateLeaf(root);

        Assert.False(GigaChatTls.Validate(root, leaf, null, errors));
    }

    [Fact]
    public void Tls_AcceptsSystemTrustedCertificate()
    {
        using var root = CreateCa("Test Root");
        using var other = CreateCa("Other Root");
        using var leaf = CreateLeaf(other);

        Assert.True(GigaChatTls.Validate(root, leaf, null, SslPolicyErrors.None));
    }
}