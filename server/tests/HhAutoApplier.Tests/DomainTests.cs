using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

public class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static Vacancy Queued()
    {
        var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
        v.Evaluate(VacancyEvaluator.Default, new FilterRules(), [ResumeProfile.Create(".NET", "C# .NET", ["c#"])]);
        return v;
    }

    [Fact]
    public void Vacancy_GoesThroughStatuses()
    {
        var v = Queued();
        Assert.Equal(VacancyStatus.Queued, v.Status);
        Assert.Equal(".NET", v.ResumeProfile);

        v.MarkApplied("PHP", Now);
        Assert.Equal((VacancyStatus.Applied, "PHP", Now), (v.Status, v.ResumeProfile, v.AppliedAt!.Value));

        v.Reject("отказ");
        Assert.Equal((VacancyStatus.Rejected, "отказ"), (v.Status, v.StatusReason));
    }

    [Fact]
    public void Vacancy_RejectsInvalidTransitions()
    {
        var v = Queued();
        Assert.Throws<DomainException>(() => v.Reject("до отклика"));
        Assert.Throws<DomainException>(() => v.Requeue());

        v.MarkApplied(null, Now);
        Assert.Throws<DomainException>(() => v.MarkApplied(null, Now));
        Assert.Throws<DomainException>(() => v.Skip("поздно"));
        Assert.Throws<DomainException>(() => v.Evaluate(VacancyEvaluator.Default, new FilterRules(), []));
    }

    [Fact]
    public void Vacancy_SkippedOrFailed_CanBeRequeued()
    {
        var v = Queued();
        v.Fail("капча");
        v.Requeue();
        Assert.Equal((VacancyStatus.Queued, null), (v.Status, v.StatusReason));
    }

    [Fact]
    public void Salary_FromColumnsAndToRub()
    {
        Assert.Null(Salary.From(null, null, "USD"));
        Assert.Equal(new Salary(100, null, "RUR"), Salary.From(100, null, null));
        Assert.Equal(270_000, Salary.From(null, 3000, "usd")!.ToRub());
        Assert.Null(Salary.From(1, null, "XXX")!.ToRub());
        Assert.Equal("от 100 до 200 RUR", new Salary(100, 200, "RUR").ToString());
    }

    [Fact]
    public void Profiles_ValidateRequiredFields()
    {
        Assert.Throws<DomainException>(() => SearchProfile.Create(" ", "C#", null, null, null, true));
        Assert.Throws<DomainException>(() => ResumeProfile.Create(".NET", "", ["c#"]));

        var search = SearchProfile.Create("dotnet", " C# ", "", "&work_format=REMOTE", null, true);
        Assert.Equal(("C#", null, "work_format=REMOTE"), (search.Query, search.Area, search.ExtraParams));
        Assert.Equal(["c#", ".net"], ResumeProfile.Create(".NET", "C#", ["c#", " .net ", "C#", ""]).Keywords);
    }
}
