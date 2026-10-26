using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Tests;

public class QuestionnaireTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly ResumeProfile DotNet = ResumeProfile.Create(".NET", "C# .NET", ["c#"], "5 лет на C#, ASP.NET Core");

    // Вопросы — как на живой странице отклика: тест на знания, согласие, свободный текст
    private static VacancyQuestion[] Questions() =>
    [
        VacancyQuestion.Create("Какой принцип  SOLID описывает зависимость от абстракций?", QuestionType.Radio,
            ["Single Responsibility Principle", "Dependency Inversion Principle"]),
        VacancyQuestion.Create("Вы принимаете это условие?", QuestionType.Checkbox, ["Да", "Нет"]),
        VacancyQuestion.Create("Укажите зарплатные ожидания", QuestionType.Text, []),
    ];

    private static JobApplication Questionnaire()
    {
        var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateDetails(null, "Пишем на ASP.NET Core", []);
        var a = JobApplication.Create(v, null, Now);
        a.Evaluate(VacancyEvaluator.Default, new FilterRules(), [DotNet], Now);
        a.RequireAnswers(Questions(), Now);
        return a;
    }

    [Fact]
    public void Application_QuestionnaireThenAnsweredToQueue()
    {
        var v = Questionnaire();
        Assert.Equal((VacancyStatus.Questionnaire, "анкета: ждёт ответов LLM"), (v.Status, v.StatusReason));
        Assert.Equal("Какой принцип SOLID описывает зависимость от абстракций?", v.Questions[0].Text);

        v.AnswerQuestions([["dependency inversion principle"], ["Да"], ["от 250 000 ₽", "обсуждаемо"]], Now);
        Assert.Equal(VacancyStatus.Queued, v.Status);
        // Вариант — в написании анкеты, по нему расширение найдёт его на странице
        Assert.Equal(["Dependency Inversion Principle"], v.Questions[0].Answer);
        Assert.Equal(["от 250 000 ₽\nобсуждаемо"], v.Questions[2].Answer);

        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Да"]], Now));
        v.MarkApplied(null, Now);
        Assert.Throws<DomainException>(() => v.RequireAnswers(Questions(), Now));
    }

    // Ответы на анкету ведут сразу в очередь: анкета не должна поднимать туда отклонённое, ждущее проверки или не оценённое
    [Fact]
    public void Application_QuestionnaireOnlyFromQueue()
    {
        JobApplication Make(bool awaitLlm)
        {
            var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
            v.UpdateDetails(null, "Пишем на ASP.NET Core", []);
            var a = JobApplication.Create(v, null, Now);
            a.Evaluate(VacancyEvaluator.Default, new FilterRules(), [DotNet], Now, awaitLlm);
            return a;
        }

        var pending = Make(awaitLlm: true);
        Assert.Throws<DomainException>(() => pending.RequireAnswers(Questions(), Now));

        var review = Make(awaitLlm: true);
        review.ApplyLlmEvaluation(LlmVerdict.Doubtful, 55, "не уверена", null, "m", Now, reviewDoubtful: true);
        Assert.Throws<DomainException>(() => review.RequireAnswers(Questions(), Now));

        review.Decline(null, Now);
        Assert.Throws<DomainException>(() => review.RequireAnswers(Questions(), Now));
        Assert.Equal(VacancyStatus.Skipped, review.Status);

        // Из очереди и повторно из анкеты — можно
        var queued = Make(awaitLlm: false);
        queued.RequireAnswers(Questions(), Now);
        queued.RequireAnswers(Questions(), Now);
        Assert.Equal(VacancyStatus.Questionnaire, queued.Status);
    }

    [Fact]
    public void Application_BadAnswersLeaveQuestionnaireUntouched()
    {
        var v = Questionnaire();
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Liskov"], ["Да"], ["готов обсудить"]], Now));
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Dependency Inversion Principle", "Single Responsibility Principle"], ["Да"], ["ок"]], Now));
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Dependency Inversion Principle"], [], ["ок"]], Now));
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Dependency Inversion Principle"], ["Да"]], Now));

        Assert.Equal(VacancyStatus.Questionnaire, v.Status);
        Assert.All(v.Questions, q => Assert.Empty(q.Answer));
    }

    [Fact]
    public void BuildRequest_HasResumeWishesAndNumberedQuestions()
    {
        var request = QuestionnaireAnswering.BuildRequest(Questionnaire(), DotNet, new FilterRules { MinSalaryRub = 250_000 });
        Assert.True(request.Json);
        var text = VacancyAssessment.Serialize(request);
        Assert.Contains("5 лет на C#", text);
        Assert.Contains("зарплата от 250000 ₽", text);
        Assert.Contains("1. [один вариант] Какой принцип SOLID", text);
        Assert.Contains("   - Dependency Inversion Principle", text);
        Assert.Contains("3. [текст] Укажите зарплатные ожидания", text);
    }

    [Fact]
    public void Parse_StringsArraysAndGarbage()
    {
        var answers = QuestionnaireAnswering.Parse("""Вот: {"answers": ["Dependency Inversion Principle", ["Да"], "от 250 000 ₽", 5]}""");
        Assert.NotNull(answers);
        Assert.Equal([["Dependency Inversion Principle"], ["Да"], ["от 250 000 ₽"], ["5"]], answers.Select(a => a.ToArray()));

        Assert.Null(QuestionnaireAnswering.Parse("не знаю"));
        Assert.Null(QuestionnaireAnswering.Parse("""{"answer": ["Да"]}"""));
        Assert.Null(QuestionnaireAnswering.Parse("{answers: ["));
    }
}
