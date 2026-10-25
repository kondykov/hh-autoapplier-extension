using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Vacancies;
using OneOf;

namespace HhAutoApplier.Application.Commands.Vacancies;

// Расширение встретило анкету работодателя: вопросы на сервер, ответы подготовит LLM
public record SubmitQuestionnaireCommand
{
    [Required]
    public required string VacancyId { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public required List<QuestionInput> Questions { get; init; }
}

public record QuestionInput
{
    [Required]
    [MaxLength(2000)]
    public required string Question { get; init; }

    public QuestionType Type { get; init; }

    public List<string> Options { get; init; } = [];
}

// На .NET 11: public union SubmitQuestionnaireResult(VacancyDto, NotFoundError, ConflictError, ValidationError);
[GenerateOneOf]
public partial class SubmitQuestionnaireResult : OneOfBase<VacancyDto, NotFoundError, ConflictError, ValidationError>;
