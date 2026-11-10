using System.Text;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Llm;

// Сообщает администраторам, что модель часто даёт негодные ответы: заглушки, выдуманные цифры, неполные анкеты, не разобранный текст (#67).
// Считается по журналу LlmCalls. Сбои провайдера и лимиты (пустая модель) сюда не входят: о них есть своё сообщение
public class LlmQualityMonitor(IAppDbContext db, Notifier notifier, TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    // Частый случай: хотя бы столько отказов за час и не меньше четверти ответов модели — единичный сбой и редкие отказы не шумят
    private const int MinProblems = 5;

    private const int MinShareDivisor = 4;

    public async Task NotifyIfFrequentAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var since = now - Window;
        var calls = await db.LlmCalls.AsNoTracking().Where(c => c.CreatedAt > since && c.Model != "")
            .Select(c => new { c.Purpose, c.Error }).ToListAsync(ct);
        var problems = calls.Where(c => c.Error is not null).ToList();
        if (problems.Count < MinProblems || problems.Count * MinShareDivisor < calls.Count)
            return;

        var text = new StringBuilder()
            .AppendLine($"LLM часто даёт ответы, которые не прошли проверку: {problems.Count} из {calls.Count} за последний час.");
        foreach (var group in problems.GroupBy(p => (p.Purpose, Reason: Reason(p.Error!))).OrderByDescending(g => g.Count()))
            text.AppendLine($"— {Title(group.Key.Purpose)}, {group.Key.Reason}: {group.Count()}");
        text.AppendLine().Append("Такие письма и анкеты не уходят сами, а ждут ручной проверки. Подробности — в журнале LlmCalls. Следующее сообщение — не раньше завтрашнего дня.");

        // Одно сообщение в сутки: при плохой модели отказы идут каждый проход
        await notifier.NotifyAdminsAsync(NotificationKind.LlmQuality, $"llm-quality:{now:yyyy-MM-dd}", text.ToString(), ct);
    }

    // До двоеточия — вид причины, после — подробности конкретного ответа
    private static string Reason(string error) => error.Split(':', 2)[0];

    private static string Title(string purpose) => purpose switch
    {
        VacancyAssessment.Purpose => "оценка вакансий",
        CoverLetterWriting.Purpose => "письма",
        QuestionnaireAnswering.Purpose => "анкеты",
        LetterRequestExtraction.Purpose => "просьбы к письму",
        DialogAnalysis.Purpose => "разбор сообщений",
        _ => purpose,
    };
}