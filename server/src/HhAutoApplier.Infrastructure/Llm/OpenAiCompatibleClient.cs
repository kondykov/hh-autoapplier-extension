using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HhAutoApplier.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Llm;

// Клиент chat/completions для OpenAI-совместимых провайдеров (Groq, OpenAI, OpenRouter, локальные серверы)
public class OpenAiCompatibleClient : ILlmClient
{
    private readonly HttpClient _http;

    private readonly LlmOptions _opt;

    private readonly LlmBudget _budget;

    private readonly ILogger<OpenAiCompatibleClient> _logger;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public OpenAiCompatibleClient(
        HttpClient http,
        IOptions<LlmOptions> options,
        LlmBudget budget,
        ILogger<OpenAiCompatibleClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _opt = options.Value;
        _http = http;
        _budget = budget;
        _logger = logger;
        _delay = delay ?? Task.Delay;
        _http.BaseAddress = new Uri(_opt.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_opt.TimeoutSeconds);
        if (IsConfigured)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.ApiKey) && !string.IsNullOrWhiteSpace(_opt.Model);

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new LlmUnavailableException("не задан ключ API (LLM_API_KEY) или модель");
        if (!_budget.TryTake(_opt.DailyRequestLimit))
            throw new LlmUnavailableException($"исчерпан дневной лимит запросов ({_opt.DailyRequestLimit})");

        var body = new ChatRequest(
            _opt.Model,
            request.Messages.Select(m => new ChatMessage(m.Role, m.Content)).ToList(),
            request.Temperature,
            request.MaxTokens,
            request.Json ? new ResponseFormat("json_object") : null);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync("chat/completions", body, ct);
            }
            catch (HttpRequestException e) when (attempt < _opt.MaxRetries)
            {
                await PauseAsync(attempt, null, $"сеть: {e.Message}", ct);
                continue;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new LlmUnavailableException($"нет ответа за {_opt.TimeoutSeconds} с");
            }
            catch (HttpRequestException e)
            {
                throw new LlmUnavailableException($"сеть: {e.Message}");
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                    return await ReadAsync(response, ct);

                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                var error = await response.Content.ReadAsStringAsync(ct);
                if (!retryable || attempt >= _opt.MaxRetries)
                    throw new LlmUnavailableException($"провайдер ответил {(int)response.StatusCode}: {Trim(error)}");

                await PauseAsync(attempt, response.Headers.RetryAfter?.Delta, $"ответ {(int)response.StatusCode}", ct);
            }
        }
    }

    private async Task<LlmResult> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
        var content = chat?.Choices?.FirstOrDefault()?.Message?.Content;
        if (content is null)
            throw new LlmUnavailableException("в ответе провайдера нет текста");

        return new LlmResult(content, chat!.Model ?? _opt.Model, chat.Usage?.PromptTokens ?? 0, chat.Usage?.CompletionTokens ?? 0);
    }

    private Task PauseAsync(int attempt, TimeSpan? retryAfter, string reason, CancellationToken ct)
    {
        var pause = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
        _logger.LogWarning("LLM: {Reason}, повтор {Attempt}/{Max} через {Pause} с", reason, attempt + 1, _opt.MaxRetries, pause.TotalSeconds);
        return _delay(pause, ct);
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;

    private record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResponseFormat? ResponseFormat);

    private record ChatMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string? Content);

    private record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private record ChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] List<ChatChoice>? Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage);

    private record ChatChoice([property: JsonPropertyName("message")] ChatMessage? Message);

    private record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
