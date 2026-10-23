using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Resumes;

// Создать профиль резюме или обновить существующий с тем же именем
public record SaveResumeCommand
{
    [Required]
    [MinLength(1)]
    public required string Name { get; init; }

    [Required]
    [MinLength(1)]
    public required string Match { get; init; }

    public List<string> Keywords { get; init; } = [];
}

// На .NET 11: public union SaveResumeResult(ResumeDto, ValidationError);
[GenerateOneOf]
public partial class SaveResumeResult : OneOfBase<ResumeDto, ValidationError>;
