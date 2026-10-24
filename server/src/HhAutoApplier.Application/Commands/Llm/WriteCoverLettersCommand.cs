using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Llm;

// Письма для вакансий в очереди, у которых письма ещё нет: лучшие по баллу первыми, не больше Limit за запуск
public record WriteCoverLettersCommand
{
    [Range(1, 200)]
    public int Limit { get; init; } = 20;
}

// На .NET 11: public union WriteCoverLettersResult(CoverLettersResultDto, LlmUnavailableError);
[GenerateOneOf]
public partial class WriteCoverLettersResult : OneOfBase<CoverLettersResultDto, LlmUnavailableError>;
