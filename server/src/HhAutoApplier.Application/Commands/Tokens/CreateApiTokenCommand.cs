using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Tokens;

// Персональный токен для расширения: только после входа по паролю — токеном или общим ключом новый не выпустить
public record CreateApiTokenCommand
{
    [Required]
    [MaxLength(100)]
    public required string Name { get; init; }
}

// На .NET 11: public union CreateApiTokenResult(CreatedApiTokenDto, ValidationError, ConflictError);
[GenerateOneOf]
public partial class CreateApiTokenResult : OneOfBase<CreatedApiTokenDto, ValidationError, ConflictError>;