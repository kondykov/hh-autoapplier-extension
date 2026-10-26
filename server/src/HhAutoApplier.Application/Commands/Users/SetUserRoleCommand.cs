using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Users;

public record SetUserRoleCommand
{
    public Guid UserId { get; init; }

    [Required]
    public required string Role { get; init; }
}

// На .NET 11: public union SetUserRoleResult(UserDto, NotFoundError, ValidationError, ConflictError);
[GenerateOneOf]
public partial class SetUserRoleResult : OneOfBase<UserDto, NotFoundError, ValidationError, ConflictError>;