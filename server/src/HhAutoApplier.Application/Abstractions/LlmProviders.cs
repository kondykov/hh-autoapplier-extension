namespace HhAutoApplier.Application.Abstractions;

public enum LlmAuth
{
    // Постоянный ключ в заголовке Bearer
    ApiKey = 0,
    // Ключ меняется на короткоживущий токен: нужна реализация ILlmTokenSource
    TokenExchange = 1,
}

// Чем провайдер отличается от обычного OpenAI-совместимого API. Клиент смотрит на эти значения, а не на имя провайдера (#69)
public sealed record LlmProviderProfile(
    string Name,
    LlmAuth Auth = LlmAuth.ApiKey,
    bool SupportsJsonObject = true,
    double MinTemperature = 0,
    bool NeedsOwnBaseUrl = false)
{
    public double Temperature(double requested) => Math.Max(requested, MinTemperature);
}

// Реестр провайдеров: новый провайдер — запись здесь, без правок клиента
public static class LlmProviders
{
    public static readonly IReadOnlyDictionary<string, LlmProviderProfile> All = new[]
    {
        new LlmProviderProfile(LlmOptions.OpenAiProvider),
    }.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
}