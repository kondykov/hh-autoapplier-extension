using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HhAutoApplier.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Llm;

// Меняет ключ авторизации ГигаЧата на токен доступа (~30 минут) и держит его в памяти. Один на приложение: кэш общий для всех запросов.
// Ни ключ, ни токен, ни тело ответа OAuth не попадают в сообщения об ошибках
public sealed class GigaChatTokenSource : ILlmTokenSource
{
    public const string HttpClientName = "GigaChatOAuth";

    // Токен обновляется заранее, чтобы не истёк по дороге к провайдеру
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(2);

    // Меньше этого значения expires_at — секунды, больше — миллисекунды: в описании API единица не указана, а пример в нём похож на секунды
    private const long MillisecondsThreshold = 100_000_000_000;

    private readonly IHttpClientFactory _factory;

    private readonly LlmOptions _opt;

    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _exchange = new(1, 1);

    private volatile CachedToken? _cached;

    public GigaChatTokenSource(IHttpClientFactory factory, IOptions<LlmOptions> options, TimeProvider clock)
    {
        _factory = factory;
        _opt = options.Value;
        _clock = clock;
    }

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (Current() is { } token)
            return token;

        // Параллельные запросы ждут один обмен, а не шлют каждый свой
        await _exchange.WaitAsync(ct);
        try
        {
            if (Current() is { } exchanged)
                return exchanged;

            var fresh = await ExchangeAsync(ct);
            _cached = fresh;
            return fresh.Value;
        }
        finally
        {
            _exchange.Release();
        }
    }

    public void Invalidate(string token)
    {
        var cached = _cached;
        if (cached?.Value == token)
            Interlocked.CompareExchange(ref _cached, null, cached);
    }

    private string? Current() => _cached is { } cached && _clock.GetUtcNow() < cached.ExpiresAt - ExpiryMargin ? cached.Value : null;

    private async Task<CachedToken> ExchangeAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _opt.AuthUrl)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("scope", _opt.Scope)]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _opt.ApiKey!.Trim());
        request.Headers.Add("RqUID", Guid.NewGuid().ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var http = _factory.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(_opt.TimeoutSeconds);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new LlmUnavailableException("ГигаЧат отклонил ключ авторизации (401): проверьте LLM_API_KEY и LLM_SCOPE");

            if (!response.IsSuccessStatusCode)
                throw new LlmUnavailableException($"ГигаЧат: получение токена, ответ {(int)response.StatusCode}");

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (string.IsNullOrEmpty(body?.AccessToken))
                throw new LlmUnavailableException("ГигаЧат: в ответе на получение токена нет access_token");

            return new CachedToken(body.AccessToken, ToInstant(body.ExpiresAt));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmUnavailableException($"ГигаЧат: нет ответа на получение токена за {_opt.TimeoutSeconds} с");
        }
        catch (HttpRequestException e)
        {
            throw new LlmUnavailableException($"ГигаЧат: получение токена, сеть: {e.Message}");
        }
        catch (JsonException)
        {
            throw new LlmUnavailableException("ГигаЧат: неверный формат ответа на получение токена");
        }
    }

    private static DateTimeOffset ToInstant(long expiresAt) =>
        expiresAt >= MillisecondsThreshold ? DateTimeOffset.FromUnixTimeMilliseconds(expiresAt) : DateTimeOffset.FromUnixTimeSeconds(expiresAt);

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_at")] long ExpiresAt);
}