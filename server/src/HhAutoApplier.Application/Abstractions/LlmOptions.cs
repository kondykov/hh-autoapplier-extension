namespace HhAutoApplier.Application.Abstractions;

// Провайдер LLM с OpenAI-совместимым API. По умолчанию — Groq (решение владельца в #4); меняется адресом и моделью в конфиге
public class LlmOptions
{
    public const string Section = "Llm";

    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    // Только из .env (LLM_API_KEY); пустой — LLM выключена
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "llama-3.3-70b-versatile";

    public int TimeoutSeconds { get; set; } = 60;

    // Повторы при 429 и 5xx; пауза — Retry-After или 1, 2, 4… с
    public int MaxRetries { get; set; } = 3;

    // Защита бесплатного тарифа и кошелька: запросов в сутки (UTC); 0 — без ограничения
    public int DailyRequestLimit { get; set; } = 300;
}
