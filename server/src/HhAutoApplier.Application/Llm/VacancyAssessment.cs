using System.Text;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Llm;

public record LlmAssessment(LlmVerdict Verdict, int Score, string? Resume, string Reason);

// Промпт LLM-оценки вакансии под резюме и разбор ответа. Чистые функции — без LLM и БД
public static class VacancyAssessment
{
    public const string Purpose = "vacancy-evaluation";

    private const int MaxDescription = 3000;

    // gpt-oss сначала рассуждает: запас токенов, чтобы JSON-ответ не обрезался
    private const int MaxTokens = 1500;

    private const string System = """
        Ты помогаешь соискателю отбирать вакансии. Оцени, насколько вакансия подходит кандидату с учётом его резюме и правил поиска.
        Ответь только JSON-объектом без пояснений вокруг:
        {"verdict": "fit" | "doubtful" | "reject", "score": 0-100, "resume": "<имя резюме из списка или null>", "reason": "1–2 предложения по-русски"}
        fit — стоит откликаться; doubtful — есть сомнения (роль, уровень, стек совпадают частично, неясные условия);
        reject — явно не подходит (другая профессия или роль, уровень, основной стек, условия).
        resume — какое резюме из списка лучше отправить; null, если не подходит ни одно.
        """;

    public static LlmRequest BuildRequest(Vacancy vacancy, IReadOnlyCollection<ResumeProfile> resumes, FilterRules rules) =>
        new([LlmMessage.System(System), LlmMessage.User(Describe(vacancy, resumes, rules))], Temperature: 0.1, MaxTokens: MaxTokens, Json: true);

    private static string Describe(Vacancy vacancy, IReadOnlyCollection<ResumeProfile> resumes, FilterRules rules)
    {
        var text = new StringBuilder();
        text.AppendLine("Резюме кандидата:");
        foreach (var r in resumes)
            text.AppendLine($"- {r.Name}: {r.Summary ?? "подробного описания нет, ключевые слова: " + string.Join(", ", r.Keywords)}");

        var limits = new List<string>();
        if (rules.ExcludeKeywords.Count > 0)
            limits.Add($"не интересно: {string.Join(", ", rules.ExcludeKeywords)}");
        if (rules.MinSalaryRub is { } min)
            limits.Add($"зарплата от {min} ₽");
        if (rules.AllowedFormats.Count > 0)
            limits.Add($"формат: {string.Join(", ", rules.AllowedFormats)}");
        if (limits.Count > 0)
            text.AppendLine().AppendLine($"Пожелания кандидата: {string.Join("; ", limits)}.");

        var description = vacancy.Description.Length > MaxDescription ? vacancy.Description[..MaxDescription] + "…" : vacancy.Description;
        text.AppendLine()
            .AppendLine("Вакансия:")
            .AppendLine($"Название: {vacancy.Title}")
            .AppendLine($"Компания: {vacancy.Company}")
            .AppendLine($"Зарплата: {vacancy.Salary?.ToString() ?? "не указана"}")
            .AppendLine($"Формат: {(vacancy.WorkFormat == "" ? "не указан" : vacancy.WorkFormat)}")
            .AppendLine($"Стек: {(vacancy.Stack.Length == 0 ? "не выделен" : string.Join(", ", vacancy.Stack))}")
            .AppendLine($"Тестовое задание: {(vacancy.HasTest ? "есть" : "нет")}")
            .AppendLine("Описание:")
            .Append(description);
        return text.ToString();
    }

    // null — ответ не разобран (не JSON, нет вердикта)
    public static LlmAssessment? Parse(string content, IReadOnlyCollection<ResumeProfile> resumes)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var json = JsonDocument.Parse(content[start..(end + 1)]);
            var root = json.RootElement;
            var verdict = Str(root, "verdict")?.Trim().ToLowerInvariant() switch
            {
                "fit" => LlmVerdict.Fit,
                "doubtful" => LlmVerdict.Doubtful,
                "reject" => LlmVerdict.Reject,
                _ => (LlmVerdict?)null,
            };
            if (verdict is null)
                return null;

            var score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var n) ? n
                : int.TryParse(Str(root, "score"), out var parsed) ? parsed
                : verdict == LlmVerdict.Fit ? 70 : verdict == LlmVerdict.Doubtful ? 50 : 10;

            // Резюме — только из списка; иначе остаётся выбор правил
            var resumeName = Str(root, "resume");
            var resume = resumes.FirstOrDefault(r => string.Equals(r.Name, resumeName?.Trim(), StringComparison.OrdinalIgnoreCase))?.Name;

            var reason = Str(root, "reason")?.Trim();
            return new LlmAssessment(verdict.Value, Math.Clamp(score, 0, 100), resume, string.IsNullOrEmpty(reason) ? "без объяснения" : reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Промпт целиком — для журнала LlmCalls
    public static string Serialize(LlmRequest request) =>
        string.Join("\n\n", request.Messages.Select(m => $"[{m.Role}]\n{m.Content}"));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
