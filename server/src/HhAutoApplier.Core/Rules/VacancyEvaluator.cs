using HhAutoApplier.Core.Domain;

namespace HhAutoApplier.Core.Rules;

public record Evaluation(VacancyStatus Status, int Score, string? Reason, string? ResumeProfile);

public class VacancyEvaluator(IReadOnlyDictionary<string, decimal> rubRates)
{
    public static readonly IReadOnlyDictionary<string, decimal> DefaultRates = new Dictionary<string, decimal>
    {
        ["RUR"] = 1, ["RUB"] = 1, ["USD"] = 90, ["EUR"] = 100, ["KZT"] = 0.18m, ["BYR"] = 30, ["BYN"] = 30,
    };

    public static VacancyEvaluator Default { get; } = new(DefaultRates);

    public int? SalaryRub(Vacancy v)
    {
        var amount = v.SalaryTo ?? v.SalaryFrom;
        if (amount is null) return null;
        var cur = (v.SalaryCurrency ?? "RUR").ToUpperInvariant();
        return rubRates.TryGetValue(cur, out var rate) ? (int)(amount.Value * rate) : null;
    }

    public Evaluation Evaluate(Vacancy v, FilterRules rules, IEnumerable<ResumeProfile> resumes)
    {
        var haystack = $"{v.Title} {v.Description} {string.Join(' ', v.Stack)}";
        var text = TextNormalizer.Normalize(haystack);
        var profile = ResumePicker.Pick(resumes, haystack);

        var exclude = rules.ExcludeKeywords.FirstOrDefault(k => text.Contains(TextNormalizer.Normalize(k)));
        if (exclude is not null) return Rejected($"стоп-слово «{exclude}»");

        var included = rules.IncludeKeywords.Where(k => text.Contains(TextNormalizer.Normalize(k))).ToList();
        if (rules.IncludeKeywords.Count > 0 && included.Count == 0) return Rejected("нет ни одного нужного слова");

        if (rules.SkipWithTest && v.HasTest) return Rejected("есть тестовое задание");

        var salary = SalaryRub(v);
        if (rules.RequireSalary && salary is null) return Rejected("зарплата не указана");
        if (rules.MinSalaryRub is { } min && salary is { } s && s < min) return Rejected($"зарплата {s} ниже {min}");

        if (rules.AllowedFormats.Count > 0 && !string.IsNullOrWhiteSpace(v.WorkFormat))
        {
            var fmt = TextNormalizer.Normalize(v.WorkFormat);
            if (!rules.AllowedFormats.Any(f => fmt.Contains(TextNormalizer.Normalize(f))))
                return Rejected($"формат «{v.WorkFormat}» не подходит");
        }

        var score = 50 + Math.Min(included.Count, 3) * 10 + (salary is not null ? 10 : 0)
                    + Math.Min(v.Stack.Length, 4) * 3 + (profile is not null ? 10 : 0);
        if (score < rules.MinScore) return Rejected($"оценка {score} ниже {rules.MinScore}");

        return new Evaluation(VacancyStatus.Queued, score, null, profile?.Name);

        Evaluation Rejected(string? reason) => new(VacancyStatus.Skipped, 0, reason, profile?.Name);
    }
}
