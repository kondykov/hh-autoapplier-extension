using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Llm;

// Ответы LLM на анкеты работодателей (Questionnaire): лучшие по баллу первыми, не больше Limit за запуск
public record AnswerQuestionnairesCommand
{
    [Range(1, 200)]
    public int Limit { get; init; } = 20;
}

// На .NET 11: public union AnswerQuestionnairesResult(QuestionnaireAnswersResultDto, LlmUnavailableError);
[GenerateOneOf]
public partial class AnswerQuestionnairesResult : OneOfBase<QuestionnaireAnswersResultDto, LlmUnavailableError>;
