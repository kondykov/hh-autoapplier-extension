using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Users;

// Пользователя заводит администратор: самостоятельной регистрации нет. Пароль временный — пользователь меняет его сам
public record CreateUserCommand
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public required string Email { get; init; }

    [Required]
    [MaxLength(128)]
    public required string Password { get; init; }

    // Admin или User
    [Required]
    public required string Role { get; init; }
}

// На .NET 11: public union CreateUserResult(UserDto, ValidationError);
[GenerateOneOf]
public partial class CreateUserResult : OneOfBase<UserDto, ValidationError>;