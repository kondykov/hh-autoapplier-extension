using System.Text;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Dialogs;
using HhAutoApplier.Domain.Resumes;

namespace HhAutoApplier.Application.Llm;

public record LlmDialogAnalysis(DialogKind Kind, string Summary, string? Reply);

// Промпт разбора ответа работодателя (#26) и разбор ответа модели. Чистые функции — без LLM и БД
public static class DialogAnalysis
{
    public const string Purpose = "dialog-analysis";

    // gpt-oss сначала рассуждает: запас токенов, чтобы JSON-ответ не обрезался
    private const int MaxTokens = 1500;

    private const string System = """
        Ты помогаешь соискателю разбирать сообщения работодателей в чатах откликов hh.ru.
        Определи, что написал работодатель, кратко перескажи суть и предложи ответ от имени кандидата.
        Ответь только JSON-объектом без пояснений вокруг:
        {"kind": "invitation" | "question" | "rejection" | "other", "summary": "1–2 предложения по-русски", "reply": "текст ответа или null"}
        invitation — приглашение на собеседование, звонок, тестовое задание или следующий этап;
        question — работодатель что-то спрашивает или просит (опыт, зарплата, контакты, удобное время);
        rejection — отказ; other — остальное (автоматическое уведомление, просмотр резюме).
        summary — что хочет работодатель и что от кандидата требуется (сроки, время, ссылка — если есть в сообщении).
        reply — короткий вежливый ответ по-русски, 1–4 предложения, без подписи и контактов.
        Только факты из сообщения, резюме и профиля кандидата: не придумывай опыт, цифры, даты и время. Не приписывай кандидату роли, навыки и опыт,
        которых нет в резюме дословно (например, управление проектами у разработчика); спрашивают про такой опыт — заглушка [ваш опыт …].
        Где нужен ответ кандидата
        (удобное время, ожидания по зарплате), оставь заметную заглушку в квадратных скобках, например [удобное время].
        Для rejection и other reply — null, если отвечать по существу нечего.
        """;

    private const int MaxMessage = 3000;

    public static LlmRequest BuildRequest(EmployerMessage message, ResumeProfile? resume, CandidateProfile? profile = null) =>
        new([LlmMessage.System(System), LlmMessage.User(Describe(message, resume, profile))], Temperature: 0.2, MaxTokens: MaxTokens, Json: true);

    private static string Describe(EmployerMessage message, ResumeProfile? resume, CandidateProfile? profile)
    {
        var text = new StringBuilder()
            .AppendLine($"Вакансия: {(message.Title == "" ? "не указана" : message.Title)}")
            .AppendLine($"Компания: {(message.Company == "" ? "не указана" : message.Company)}")
            .AppendLine($"Состояние отклика на hh.ru: {message.HhState ?? "не указано"}");
        if (resume is not null)
            text.AppendLine($"Резюме кандидата ({resume.Name}): {resume.Summary ?? "описания нет, ключевые слова: " + string.Join(", ", resume.Keywords)}");
        // Факты — по навыкам, о которых пишет работодатель
        if (CandidateContext.Describe(profile, $"{message.Title}\n{message.Text}") is { } candidate)
            text.AppendLine(candidate);

        var body = message.Text.Length > MaxMessage ? message.Text[..MaxMessage] + "…" : message.Text;
        return text.AppendLine().AppendLine("Сообщение работодателя:").Append(body).ToString();
    }

    // null — ответ не разобран (не JSON, нет вида)
    public static LlmDialogAnalysis? Parse(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var json = JsonDocument.Parse(content[start..(end + 1)]);
            var root = json.RootElement;
            var kind = Str(root, "kind")?.Trim().ToLowerInvariant() switch
            {
                "invitation" => DialogKind.Invitation,
                "question" => DialogKind.Question,
                "rejection" => DialogKind.Rejection,
                "other" => DialogKind.Other,
                _ => (DialogKind?)null,
            };
            if (kind is null)
                return null;

            var summary = Str(root, "summary")?.Trim();
            var reply = Str(root, "reply")?.Trim();
            return new LlmDialogAnalysis(kind.Value, string.IsNullOrEmpty(summary) ? "без пересказа" : summary,
                string.IsNullOrEmpty(reply) || reply.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : reply.Replace("\r\n", "\n"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
