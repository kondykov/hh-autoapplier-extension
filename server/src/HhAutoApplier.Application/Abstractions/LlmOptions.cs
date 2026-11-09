namespace HhAutoApplier.Application.Abstractions;

// Провайдер LLM с OpenAI-совместимым API. По умолчанию — Groq (решение владельца в #4); меняется адресом и моделью в конфиге
public class LlmOptions
{
    public const string Section = "Llm";

    public const string OpenAiProvider = "openai";

    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1";

    public const string JsonObjectMode = "json_object";

    public const string NoJsonMode = "none";

    // Запись реестра LlmProviders: способ входа и возможности провайдера (#69)
    public string Provider { get; set; } = OpenAiProvider;

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    // Только из .env (LLM_API_KEY); пустой — LLM выключена
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "openai/gpt-oss-120b";

    public int TimeoutSeconds { get; set; } = 60;

    // Повторы при 429 и 5xx; пауза — Retry-After или 1, 2, 4… с
    public int MaxRetries { get; set; } = 3;

    // Защита бесплатного тарифа и кошелька: запросов в сутки (UTC); 0 — без ограничения
    public int DailyRequestLimit { get; set; } = 300;

    // Доля одного пользователя в дневном лимите (#56, решение владельца 2026-11-06); 0 — без доли
    public int UserDailyRequestLimit { get; set; } = 150;

    // json_object или none (JSON задаётся промптом); пусто — как в профиле. Поддержка зависит и от конкретной модели
    public string? JsonMode { get; set; }

    // Методы, а не свойства: привязка конфигурации не должна читать профиль, пока Provider не задан
    public LlmProviderProfile GetProfile() => LlmProviders.All[ProviderName()];

    public bool UsesJsonObject() => string.IsNullOrWhiteSpace(JsonMode)
        ? GetProfile().SupportsJsonObject
        : string.Equals(JsonMode, JsonObjectMode, StringComparison.OrdinalIgnoreCase);

    public void EnsureValid() => EnsureValid(LlmProviders.All);

    // Ошибка конфигурации видна при старте: опечатка в провайдере не должна молча включать другой способ входа,
    // а ключ другого провайдера не должен уйти на адрес Groq, оставшийся по умолчанию
    public void EnsureValid(IReadOnlyDictionary<string, LlmProviderProfile> profiles)
    {
        if (!profiles.TryGetValue(ProviderName(), out var profile))
            throw new InvalidOperationException($"Llm:Provider: неизвестный провайдер «{Provider}», допустимы {string.Join(", ", profiles.Keys)}");

        if (!string.IsNullOrWhiteSpace(JsonMode)
            && !string.Equals(JsonMode, JsonObjectMode, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(JsonMode, NoJsonMode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Llm:JsonMode: неизвестный режим «{JsonMode}», допустимы {JsonObjectMode} и {NoJsonMode}");

        if (profile.NeedsOwnBaseUrl && string.Equals(BaseUrl.TrimEnd('/'), DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Для Llm:Provider={profile.Name} задайте адрес провайдера в Llm:BaseUrl (LLM_BASE_URL)");
    }

    private string ProviderName() => string.IsNullOrWhiteSpace(Provider) ? OpenAiProvider : Provider;
}