using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Errors;
using OneOf;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Users;

// Администратор задаёт новый временный пароль (пользователь забыл свой); открытые сессии пользователя обрываются
public record ResetUserPasswordCommand
{
    public Guid UserId { get; init; }

    [Required]
    [MaxLength(128)]
    public required string Password { get; init; }
}

// На .NET 11: public union ResetUserPasswordResult(Success, NotFoundError, ValidationError);
[GenerateOneOf]
public partial class ResetUserPasswordResult : OneOfBase<Success, NotFoundError, ValidationError>;