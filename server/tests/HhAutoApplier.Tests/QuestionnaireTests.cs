using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
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

    private static Vacancy Questionnaire()
    {
        var v = Vacancy.Create("1", "C# разработчик", "https://hh.ru/vacancy/1", "dotnet", Now);
        v.UpdateDetails(null, "Пишем на ASP.NET Core", []);
        v.Evaluate(VacancyEvaluator.Default, new FilterRules(), [DotNet]);
        v.RequireAnswers(Questions());
        return v;
    }

    [Fact]
    public void Vacancy_QuestionnaireThenAnsweredToQueue()
    {
        var v = Questionnaire();
        Assert.Equal((VacancyStatus.Questionnaire, "анкета: ждёт ответов LLM"), (v.Status, v.StatusReason));
        Assert.Equal("Какой принцип SOLID описывает зависимость от абстракций?", v.Questions[0].Text);

        v.AnswerQuestions([["dependency inversion principle"], ["Да"], ["от 250 000 ₽", "обсуждаемо"]]);
        Assert.Equal(VacancyStatus.Queued, v.Status);
        // Вариант — в написании анкеты, по нему расширение найдёт его на странице
        Assert.Equal(["Dependency Inversion Principle"], v.Questions[0].Answer);
        Assert.Equal(["от 250 000 ₽\nобсуждаемо"], v.Questions[2].Answer);

        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Да"]]));
        v.MarkApplied(null, Now);
        Assert.Throws<DomainException>(() => v.RequireAnswers(Questions()));
    }

    [Fact]
    public void Vacancy_BadAnswersLeaveQuestionnaireUntouched()
    {
        var v = Questionnaire();
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Liskov"], ["Да"], ["готов обсудить"]]));
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Dependency Inversion Principle", "Single Responsibility Principle"], ["Да"], ["ок"]]));
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Dependency Inversion Principle"], [], ["ок"]]));
        Assert.Throws<DomainException>(() => v.AnswerQuestions([["Dependency Inversion Principle"], ["Да"]]));

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
