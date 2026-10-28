namespace HhAutoApplier.Domain.Llm;

// Журнал запросов к LLM: промпт, ответ, модель и токены — для разбора ошибок и учёта дневного лимита
public class LlmCall
{
    // Для EF
    private LlmCall()
    {
    }

    public long Id { get; private set; }

    // Зачем был запрос: check, vacancy-evaluation, …
    public string Purpose { get; private set; } = "";

    public string? VacancyId { get; private set; }

    public string Model { get; private set; } = "";

    public string Prompt { get; private set; } = "";

    public string? Response { get; private set; }

    public int PromptTokens { get; private set; }

    public int CompletionTokens { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static LlmCall Succeeded(string purpose, string? vacancyId, string model, string prompt, string response, int promptTokens, int completionTokens, DateTimeOffset at) =>
        new()
        {
            Purpose = purpose,
            VacancyId = vacancyId,
            Model = model,
            Prompt = prompt,
            Response = response,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CreatedAt = at,
        };

    // Модель ответила, но ответ не принят (например, цифры в письме не из фактов): ответ и причина — в журнал
    public static LlmCall Rejected(string purpose, string? vacancyId, string model, string prompt, string response, string error, int promptTokens,
        int completionTokens, DateTimeOffset at)
    {
        var call = Succeeded(purpose, vacancyId, model, prompt, response, promptTokens, completionTokens, at);
        call.Error = error;
        return call;
    }

    public static LlmCall Failed(string purpose, string? vacancyId, string model, string prompt, string error, DateTimeOffset at) =>
        new()
        {
            Purpose = purpose,
            VacancyId = vacancyId,
            Model = model,
            Prompt = prompt,
            Error = error,
            CreatedAt = at,
        };
}
