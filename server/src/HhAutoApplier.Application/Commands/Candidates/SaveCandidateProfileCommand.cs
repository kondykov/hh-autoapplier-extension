using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Candidates;

// Сохранить свой профиль кандидата целиком: общая часть и факты по навыкам (пустые строки навыков пропускаются)
public record SaveCandidateProfileCommand
{
    [MaxLength(4000)]
    public string? General { get; init; }

    public List<SkillFactDto> Skills { get; init; } = [];
}

// На .NET 11: public union SaveCandidateProfileResult(CandidateProfileDto, ValidationError);
[GenerateOneOf]
public partial class SaveCandidateProfileResult : OneOfBase<CandidateProfileDto, ValidationError>;
