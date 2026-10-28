using System.Text;
using HhAutoApplier.Domain.Candidates;

namespace HhAutoApplier.Application.Llm;

// Профиль кандидата в промпте (#35): общая часть и факты только по навыкам этой вакансии. Чистая функция — без LLM и БД
public static class CandidateContext
{
    // null — профиля нет или он пуст: промпт как раньше, по резюме
    public static string? Describe(CandidateProfile? profile, string vacancyText, IEnumerable<string>? vacancyStack = null)
    {
        if (profile is null || profile.IsEmpty)
            return null;

        var text = new StringBuilder("Профиль кандидата (факты о нём наравне с резюме):\n");
        if (profile.General is { } general)
            text.AppendLine(general);

        var skills = profile.SkillsFor(vacancyText, vacancyStack);
        if (skills.Count > 0)
        {
            text.AppendLine("Факты по навыкам этой вакансии:");
            foreach (var s in skills)
                text.AppendLine($"- {s.Skill}: {s.Facts}");
        }
        else
        {
            text.AppendLine("Фактов по навыкам этой вакансии в профиле нет.");
        }

        return text.ToString().TrimEnd();
    }
}
