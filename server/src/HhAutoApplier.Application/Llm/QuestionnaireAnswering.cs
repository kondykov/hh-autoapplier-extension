using System.Text;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Llm;

// Промпт ответов на анкету работодателя и разбор ответа. Чистые функции — без LLM и БД
public static class QuestionnaireAnswering
{
    public const string Purpose = "questionnaire";

    private const int MaxDescription = 2000;

    // gpt-oss сначала рассуждает: запас токенов, чтобы JSON-ответ не обрезался
    private const int MaxTokens = 3000;

    private const string System = """
        Ты помогаешь соискателю ответить на анкету работодателя при отклике на hh.ru.
        Ответь только JSON-объектом без пояснений вокруг: {"answers": [<ответ на вопрос 1>, <ответ на вопрос 2>, …]} — ровно по одному ответу на каждый вопрос, в том же порядке.
        Ответ на вопрос с одним вариантом — строка с текстом варианта дословно; с несколькими вариантами — массив строк с текстами вариантов дословно; на текстовый вопрос — строка по-русски, 1–3 предложения.
        Вопросы на знания (тест) — выбери правильный вариант.
        Вопросы о кандидате — отвечай только по резюме, профилю кандидата и пожеланиям: не выдумывай опыт, проекты, ссылки и цифры.
        Зарплатные ожидания — по пожеланиям кандидата, если их нет — «готов обсудить». Если нужного факта нет — честно и коротко: «расскажу на собеседовании».
        """;

    public static LlmRequest BuildRequest(JobApplication application, ResumeProfile? resume, FilterRules rules, CandidateProfile? profile = null) =>
        new([LlmMessage.System(System), LlmMessage.User(Describe(application.Vacancy, application.Questions, resume, rules, profile))], Temperature: 0.1, MaxTokens: MaxTokens, Json: true);

    private static string Describe(Vacancy vacancy, IReadOnlyList<VacancyQuestion> questions, ResumeProfile? resume, FilterRules rules, CandidateProfile? profile)
    {
        var text = new StringBuilder();
        text.AppendLine(resume is null
            ? "Резюме кандидата: не выбрано."
            : $"Резюме кандидата ({resume.Name}): {resume.Summary ?? "подробного описания нет, ключевые слова: " + string.Join(", ", resume.Keywords)}");
        // Вопросы анкеты — тоже про стек: по ним подбираются факты
        if (CandidateContext.Describe(profile, $"{vacancy.Title}\n{vacancy.Description}\n{string.Join("\n", questions.Select(q => q.Text))}") is { } candidate)
            text.AppendLine(candidate);

        var wishes = new List<string>();
        if (rules.MinSalaryRub is { } min)
            wishes.Add($"зарплата от {min} ₽");
        if (rules.AllowedFormats.Count > 0)
            wishes.Add($"формат: {string.Join(", ", rules.AllowedFormats)}");
        text.AppendLine($"Пожелания кандидата: {(wishes.Count > 0 ? string.Join("; ", wishes) : "не указаны")}.");

        var description = vacancy.Description.Length > MaxDescription ? vacancy.Description[..MaxDescription] + "…" : vacancy.Description;
        text.AppendLine()
            .AppendLine($"Вакансия: {vacancy.Title}, {(vacancy.Company == "" ? "компания не указана" : vacancy.Company)}, {(vacancy.Area == "" ? "город не указан" : vacancy.Area)}")
            .AppendLine(description)
            .AppendLine()
            .AppendLine("Анкета:");

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var kind = q.Type switch
            {
                QuestionType.Radio => "один вариант",
                QuestionType.Checkbox => "несколько вариантов",
                _ => "текст",
            };
            text.AppendLine($"{i + 1}. [{kind}] {q.Text}");
            foreach (var option in q.Options)
                text.AppendLine($"   - {option}");
        }

        return text.ToString();
    }

    // Ответы по порядку вопросов; null — ответ не разобран (не JSON, нет массива answers).
    // Соответствие вариантам проверяет агрегат
    public static IReadOnlyList<IReadOnlyCollection<string>>? Parse(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var json = JsonDocument.Parse(content[start..(end + 1)]);
            if (!json.RootElement.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Array)
                return null;

            return answers.EnumerateArray().Select(Values).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyCollection<string> Values(JsonElement answer) => answer.ValueKind switch
    {
        JsonValueKind.String => [answer.GetString()!],
        JsonValueKind.Number => [answer.GetRawText()],
        JsonValueKind.Array => answer.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList(),
        _ => [],
    };
}
