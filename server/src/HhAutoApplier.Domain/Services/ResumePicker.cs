using HhAutoApplier.Domain.Resumes;

namespace HhAutoApplier.Domain.Services;

public static class ResumePicker
{
    // Побеждает профиль с наибольшим числом совпавших ключевых слов; при нуле или ничьей лидеров — null
    public static ResumeProfile? Pick(IEnumerable<ResumeProfile> profiles, string haystack)
    {
        var text = TextNormalizer.Normalize(haystack);
        var scored = profiles
            .Select(p => (Profile: p, Score: p.Keywords.Count(k => text.Contains(TextNormalizer.Normalize(k)))))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ToList();
        switch (scored.Count)
        {
            case 0:
            case > 1 when scored[0].Score == scored[1].Score:
                return null;
            default:
                return scored[0].Profile;
        }
    }
}
