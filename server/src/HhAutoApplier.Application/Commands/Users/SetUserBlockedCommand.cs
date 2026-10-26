using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Users;

// Заблокировать — вход закрыт, открытые сессии обрываются; разблокировать — вход снова открыт
public record SetUserBlockedCommand
{
    public Guid UserId { get; init; }

    public bool Blocked { get; init; }
}

// На .NET 11: public union SetUserBlockedResult(UserDto, NotFoundError, ConflictError);
[GenerateOneOf]
public partial class SetUserBlockedResult : OneOfBase<UserDto, NotFoundError, ConflictError>;