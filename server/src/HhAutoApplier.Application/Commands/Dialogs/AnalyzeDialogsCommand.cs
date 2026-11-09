using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Dialogs;

// Разбор LLM новых сообщений работодателей (кроме отказов): вид, пересказ, черновик ответа; приглашения и вопросы — в Telegram
public record AnalyzeDialogsCommand
{
    [Range(1, 200)]
    public int Limit { get; init; } = 20;
}

// На .NET 11: public union AnalyzeDialogsResult(DialogsAnalysisResultDto, LlmUnavailableError);
[GenerateOneOf]
public partial class AnalyzeDialogsResult : OneOfBase<DialogsAnalysisResultDto, LlmUnavailableError>;
