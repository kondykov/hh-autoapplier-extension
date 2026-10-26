using System.Text;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Dialogs;

namespace HhAutoApplier.Application.Notifications;

// Тексты бота. Простой текст без разметки: названия вакансий и письма не нужно экранировать
public static class BotTexts
{
    public const string Help = """
        Команды:
        /status — очередь, проверка и отклики за сутки
        /review — отклики на проверке с кнопками «Одобрить» и «Отклонить»
        /stop — отвязать чат

        Уведомления: отклик на проверке, приглашения и вопросы работодателей с черновиком ответа, капча и дневной лимит в расширении, анкета без ответа LLM; администратору — сбои сбора и LLM.
        """;

    public const string NotLinked = "Чат не привязан. Получите код на backend (POST /api/telegram/link после входа по паролю) и отправьте сюда: /start <код>";

    public const string Approve = "approve";

    public const string Decline = "decline";

    // Сомнительный отклик для решения в чате; Data кнопок — «действие:id вакансии» (лимит Telegram — 64 байта)
    public static BotMessage Review(JobApplication application)
    {
        var v = application.Vacancy;
        var text = new StringBuilder()
            .AppendLine($"На проверке: {v.Title}{(string.IsNullOrWhiteSpace(v.Company) ? "" : $" — {v.Company}")}")
            .AppendLine(v.Url)
            .AppendLine($"LLM {application.LlmScore ?? application.Score}: {application.LlmReason ?? application.StatusReason ?? "без пояснения"}");
        if (!string.IsNullOrWhiteSpace(application.ResumeProfile))
            text.AppendLine($"Резюме: {application.ResumeProfile}");

        text.AppendLine().AppendLine(string.IsNullOrWhiteSpace(application.CoverLetter)
            ? "Письма пока нет — расширение возьмёт свой шаблон. Поправить письмо — на /review.html."
            : $"Письмо (поправить — на /review.html):\n{application.CoverLetter}");

        return new BotMessage(text.ToString().TrimEnd(), ReviewButtons(application.VacancyId));
    }

    // Ответ работодателя с разбором LLM и черновиком; отвечает человек — в чате hh.ru
    public static string EmployerReply(EmployerMessage message)
    {
        var kind = message.Kind switch
        {
            DialogKind.Invitation => "Приглашение",
            DialogKind.Question => "Вопрос работодателя",
            DialogKind.Rejection => "Отказ",
            _ => "Сообщение работодателя",
        };
        var text = new StringBuilder()
            .AppendLine($"{kind}: {message.Title}{(string.IsNullOrWhiteSpace(message.Company) ? "" : $" — {message.Company}")}")
            .AppendLine($"https://hh.ru/chat/{message.ChatId}");
        if (!string.IsNullOrWhiteSpace(message.Summary))
            text.AppendLine($"Суть: {message.Summary}");

        text.AppendLine().AppendLine("Сообщение:").AppendLine(message.Text);
        if (!string.IsNullOrWhiteSpace(message.SuggestedReply))
            text.AppendLine().AppendLine("Черновик ответа (отправьте сами в чате, поправив [заглушки]):").AppendLine(message.SuggestedReply);

        return text.AppendLine().Append("Все ответы — на /dialogs.html").ToString();
    }

    public static IReadOnlyList<BotButton> ReviewButtons(string vacancyId) =>
    [
        new BotButton("✅ Одобрить", $"{Approve}:{vacancyId}"),
        new BotButton("❌ Отклонить", $"{Decline}:{vacancyId}"),
    ];
}
