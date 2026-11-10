using System.Globalization;
using System.Text.RegularExpressions;
using HhAutoApplier.Domain.Services;

namespace HhAutoApplier.Domain.Candidates;

// Агрегат: профиль кандидата для LLM (#35) — один на пользователя, общий для всех его резюме.
// Под вакансию в промпт идут общая часть и только факты по навыкам этой вакансии: короче промпт, меньше поводов
// смешивать факты и подменять требования вакансии опытом
public class CandidateProfile
{
    public const int MaxGeneral = 4000;

    public const int MaxSkill = 100;

    public const int MaxFacts = 2000;

    public const int MaxSkills = 60;

    public const int MaxDesiredSalaryRub = 100_000_000;

    // Для EF
    private CandidateProfile()
    {
    }

    public int Id { get; private set; }

    // Владелец; null — владелец по умолчанию, пока нет пользователей
    public Guid? UserId { get; private set; }

    // Общая часть: уровень, стаж, формат работы, город, языки, пожелания
    public string? General { get; private set; }

    public List<SkillFact> Skills { get; private set; } = [];

    // Ответы работодателю в письме (#72, В12): в промпты LLM не идут, сервер дописывает их к письму сам. null — не указано
    public int? DesiredSalaryRub { get; private set; }

    public bool? ReadyForTestTask { get; private set; }

    // LLM-контекст: зарплата и готовность к тестовому в него не входят
    public bool IsEmpty => General is null && Skills.Count == 0;

    public string? DesiredSalaryText => DesiredSalaryRub is { } salary
        ? $"{salary.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ')} ₽"
        : null;

    public string? TestTaskText => ReadyForTestTask switch
    {
        true => "Готов выполнить тестовое задание",
        false => "Выполнять тестовое задание не готов",
        null => null,
    };

    public static CandidateProfile Create(Guid? userId) => new() { UserId = userId };

    public void Update(string? general, IEnumerable<SkillFact> skills, int? desiredSalaryRub = null, bool? readyForTestTask = null)
    {
        var text = string.IsNullOrWhiteSpace(general) ? null : general.Trim();
        if (text?.Length > MaxGeneral)
            throw new DomainException($"Общая часть профиля длиннее {MaxGeneral} знаков");

        if (desiredSalaryRub is <= 0 or > MaxDesiredSalaryRub)
            throw new DomainException($"Желаемая зарплата — от 1 до {MaxDesiredSalaryRub} ₽");

        var list = new List<SkillFact>();
        foreach (var s in skills)
        {
            var skill = s.Skill.Trim();
            var facts = s.Facts.Trim();
            if (skill == "" && facts == "")
                continue;
            if (skill == "")
                throw new DomainException("У факта не указан навык");
            if (facts == "")
                throw new DomainException($"У навыка «{skill}» нет фактов");
            if (skill.Length > MaxSkill || facts.Length > MaxFacts)
                throw new DomainException($"Навык «{skill}»: название до {MaxSkill} знаков, факты до {MaxFacts}");
            if (list.Any(x => string.Equals(x.Skill, skill, StringComparison.OrdinalIgnoreCase)))
                throw new DomainException($"Навык «{skill}» указан дважды");
            list.Add(new SkillFact { Skill = skill, Facts = facts });
        }

        if (list.Count > MaxSkills)
            throw new DomainException($"Навыков больше {MaxSkills}");

        // Список пересобирается: так EF видит изменение JSON-колонки
        (General, Skills, DesiredSalaryRub, ReadyForTestTask) = (text, list, desiredSalaryRub, readyForTestTask);
    }

    // Навыки, относящиеся к вакансии: стек навыка (словарь StackExtractor) пересекается со стеком вакансии,
    // или название навыка (любой синоним) упомянуто в тексте вакансии целым словом
    public IReadOnlyList<SkillFact> SkillsFor(string vacancyText, IEnumerable<string>? vacancyStack = null)
    {
        var text = TextNormalizer.Normalize(vacancyText);
        var stack = new HashSet<string>(vacancyStack ?? StackExtractor.Extract(vacancyText), StringComparer.OrdinalIgnoreCase);
        return Skills.Where(s => StackExtractor.Extract(s.Skill).Any(stack.Contains) || Names(s.Skill).Any(n => Mentioned(text, n))).ToList();
    }

    private static IEnumerable<string> Names(string skill) =>
        skill.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(TextNormalizer.Normalize).Where(n => n != "");

    // Целым словом: «Go» не находится в «Google», «C#» — находится в «C#/.NET»
    private static bool Mentioned(string text, string name) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
}
