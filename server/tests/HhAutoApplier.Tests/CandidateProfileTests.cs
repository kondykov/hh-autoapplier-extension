using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

// Профиль кандидата (#35): проверки агрегата и подбор фактов под вакансию — без БД и LLM
public class CandidateProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static CandidateProfile Profile()
    {
        var p = CandidateProfile.Create(null);
        p.Update("Middle, 5 лет, удалённо, Тверь", [
            new SkillFact { Skill = "EF Core / Entity Framework", Facts = "AsSplitQuery, N+1, latency 300+ → 15–70 мс" },
            new SkillFact { Skill = "PostgreSQL", Facts = "партиционирование, индексы" },
            new SkillFact { Skill = "Go", Facts = "две утилиты" },
            new SkillFact { Skill = "C#", Facts = "5 лет коммерческой разработки" },
            new SkillFact { Skill = "", Facts = "" },
        ]);
        return p;
    }

    private static Vacancy Card(string title, string description)
    {
        var v = Vacancy.Create("1", title, "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateCard("Acme", null, null, null, null, null);
        v.UpdateDetails(null, description, []);
        return v;
    }

    private static string[] Skills(CandidateProfile p, string text) => p.SkillsFor(text).Select(s => s.Skill).ToArray();

    [Fact]
    public void Update_SkipsBlankRows_RejectsHalfFilledAndDuplicates()
    {
        Assert.Equal(4, Profile().Skills.Count);
        var p = CandidateProfile.Create(null);
        Assert.Throws<DomainException>(() => p.Update(null, [new SkillFact { Skill = "Docker", Facts = " " }]));
        Assert.Throws<DomainException>(() => p.Update(null, [new SkillFact { Skill = " ", Facts = "что-то" }]));
        Assert.Throws<DomainException>(() => p.Update(null, [new SkillFact { Skill = "Docker", Facts = "a" }, new SkillFact { Skill = "docker", Facts = "b" }]));

        p.Update("  ", []);
        Assert.True(p.IsEmpty);
    }

    [Fact]
    public void SkillsFor_StackSynonymsAndWholeWords()
    {
        var p = Profile();
        // Стек по словарю: «postgres» в вакансии — навык PostgreSQL (оба — SQL); EF Core — по синониму в тексте
        Assert.Equal(["EF Core / Entity Framework", "PostgreSQL", "C#"], Skills(p, "Senior C#/.NET: Entity Framework, postgres"));
        // «Go» — не часть «Google»; «C#» — не в PHP-вакансии
        Assert.DoesNotContain(Skills(p, "PHP-разработчик в Google, MySQL не нужен"), s => s is "Go" or "C#");
        Assert.Equal(["Go"], Skills(p, "Бэкенд на Go и gRPC"));
    }

    [Fact]
    public void Context_GeneralAndOnlyVacancyFacts_NullWithoutProfile()
    {
        var text = CandidateContext.Describe(Profile(), "Python-разработчик, Django")!;
        Assert.Contains("Middle, 5 лет, удалённо, Тверь", text);
        Assert.Contains("Фактов по навыкам этой вакансии в профиле нет", text);
        Assert.DoesNotContain("AsSplitQuery", text);

        Assert.Null(CandidateContext.Describe(null, "C#"));
        Assert.Null(CandidateContext.Describe(CandidateProfile.Create(null), "C#"));
    }

    [Fact]
    public void Prompts_IncludeProfileFactsForVacancyStack()
    {
        var resume = ResumeProfile.Create(".NET", "C# .NET", ["c#"], "Backend на C#");
        var vacancy = Card("C# разработчик", "ASP.NET Core, EF Core, PostgreSQL");
        var letter = VacancyAssessment.Serialize(CoverLetterWriting.BuildRequest(vacancy, resume, Profile()));
        Assert.Contains("latency 300+ → 15–70 мс", letter);
        Assert.Contains("партиционирование", letter);
        Assert.DoesNotContain("две утилиты", letter);

        var assessment = VacancyAssessment.Serialize(VacancyAssessment.BuildRequest(vacancy, [resume], new(), Profile()));
        Assert.Contains("Middle, 5 лет", assessment);
        Assert.Contains("5 лет коммерческой разработки", assessment);

        // Без профиля — как раньше
        Assert.DoesNotContain("Профиль кандидата", VacancyAssessment.Serialize(CoverLetterWriting.BuildRequest(vacancy, resume)));
    }
}
