using System.Text;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Llm;

// Промпт сопроводительного письма под вакансию и очистка ответа. Чистые функции — без LLM и БД
public static class CoverLetterWriting
{
    public const string Purpose = "cover-letter";

    private const int MaxDescription = 3000;

    // gpt-oss сначала рассуждает: запас токенов, чтобы письмо не обрезалось
    private const int MaxTokens = 2000;

    // Короче — скорее отказ или мусор, чем письмо
    private const int MinLength = 40;

    private const string System = """
        Ты помогаешь соискателю писать сопроводительные письма к откликам на hh.ru.
        Напиши письмо по-русски под конкретную вакансию: 3–6 предложений, до 900 знаков, деловой и живой тон без канцелярита.
        Упомяни 1–3 навыка или опыта кандидата, которые прямо отвечают требованиям вакансии, и назови компанию или роль.
        Опирайся только на факты из резюме и шаблона: не выдумывай опыт, проекты, цифры и навыки.
        Если дан шаблон письма — сохрани его приветствие, стиль, подпись и контакты дословно, меняй только середину под вакансию.
        Ответь только текстом письма: без темы, заголовков, markdown, кавычек вокруг и заглушек вроде [Имя].
        """;

    public static LlmRequest BuildRequest(Vacancy vacancy, ResumeProfile? resume) =>
        new([LlmMessage.System(System), LlmMessage.User(Describe(vacancy, resume))], Temperature: 0.5, MaxTokens: MaxTokens);

    private static string Describe(Vacancy vacancy, ResumeProfile? resume)
    {
        var text = new StringBuilder();
        if (resume is not null)
        {
            text.AppendLine($"Резюме кандидата ({resume.Name}):")
                .AppendLine(resume.Summary ?? $"подробного описания нет, ключевые слова: {string.Join(", ", resume.Keywords)}");
            if (resume.LetterTemplate is { } template)
                text.AppendLine().AppendLine("Шаблон письма кандидата:").AppendLine(template);
        }
        else
        {
            text.AppendLine("Резюме кандидата: не выбрано — пиши без конкретных фактов о кандидате.");
        }

        var description = vacancy.Description.Length > MaxDescription ? vacancy.Description[..MaxDescription] + "…" : vacancy.Description;
        text.AppendLine()
            .AppendLine("Вакансия:")
            .AppendLine($"Название: {vacancy.Title}")
            .AppendLine($"Компания: {(vacancy.Company == "" ? "не указана" : vacancy.Company)}")
            .AppendLine($"Стек: {(vacancy.Stack.Length == 0 ? "не выделен" : string.Join(", ", vacancy.Stack))}")
            .AppendLine("Описание:")
            .Append(description);
        return text.ToString();
    }

    // null — ответ не похож на письмо (пустой, слишком короткий, JSON)
    public static string? Parse(string content)
    {
        var text = content.Trim();
        // Модель иногда оборачивает ответ в ```…``` или кавычки
        if (text.StartsWith("```"))
        {
            var firstLine = text.IndexOf('\n');
            text = firstLine < 0 ? "" : text[(firstLine + 1)..];
            if (text.EndsWith("```"))
                text = text[..^3];
            text = text.Trim();
        }

        if (text.Length > 1 && text[0] is '"' or '«' && text[^1] is '"' or '»')
            text = text[1..^1].Trim();

        if (text.Length < MinLength || text.StartsWith('{'))
            return null;

        return text.Replace("\r\n", "\n");
    }
}
