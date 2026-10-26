using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Auth;

// Вход по email и паролю: успех — cookie сессии в ответе
public record LoginCommand
{
    [Required]
    [MaxLength(256)]
    public required string Email { get; init; }

    [Required]
    [MaxLength(128)]
    public required string Password { get; init; }
}

// На .NET 11: public union LoginResult(UserDto, UnauthorizedError);
[GenerateOneOf]
public partial class LoginResult : OneOfBase<UserDto, UnauthorizedError>;