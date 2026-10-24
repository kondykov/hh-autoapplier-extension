using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Llm;

// LLM-оценка вакансий, прошедших правила (Pending): самые старые первыми, не больше Limit за запуск
public record EvaluatePendingVacanciesCommand
{
    [Range(1, 200)]
    public int Limit { get; init; } = 20;
}

// На .NET 11: public union EvaluatePendingResult(LlmEvaluationResultDto, LlmUnavailableError);
[GenerateOneOf]
public partial class EvaluatePendingResult : OneOfBase<LlmEvaluationResultDto, LlmUnavailableError>;
