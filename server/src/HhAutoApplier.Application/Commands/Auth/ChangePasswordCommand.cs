using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Errors;
using OneOf;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Auth;

// Пользователь меняет свой пароль, например временный от администратора
public record ChangePasswordCommand
{
    [Required]
    [MaxLength(128)]
    public required string CurrentPassword { get; init; }

    [Required]
    [MaxLength(128)]
    public required string NewPassword { get; init; }
}

// На .NET 11: public union ChangePasswordResult(Success, ValidationError, ConflictError);
[GenerateOneOf]
public partial class ChangePasswordResult : OneOfBase<Success, ValidationError, ConflictError>;