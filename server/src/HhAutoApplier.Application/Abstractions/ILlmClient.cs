namespace HhAutoApplier.Application.Abstractions;

// Порт к языковой модели: оценка вакансий, письма, ответы на анкеты. Реализация — в Infrastructure (OpenAI-совместимое API)
public interface ILlmClient
{
    // Ключ и модель заданы; без них вызывать бессмысленно
    bool IsConfigured { get; }

    // LlmUnavailableException — не настроен, исчерпан дневной лимит или провайдер не ответил после повторов
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public record LlmMessage(string Role, string Content)
{
    public static LlmMessage System(string content) => new("system", content);

    public static LlmMessage User(string content) => new("user", content);
}

// Json — попросить модель ответить JSON-объектом (response_format)
public record LlmRequest(IReadOnlyList<LlmMessage> Messages, double Temperature = 0.2, int MaxTokens = 800, bool Json = false);

// Модель — та, что ответила (провайдер может подставить версию), токены — для учёта расходов
public record LlmResult(string Content, string Model, int PromptTokens, int CompletionTokens);

public class LlmUnavailableException(string reason) : Exception($"LLM недоступна: {reason}");
