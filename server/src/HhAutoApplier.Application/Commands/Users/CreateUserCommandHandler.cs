using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Users;

namespace HhAutoApplier.Application.Commands.Users;

public class CreateUserCommandHandler(IUserService users) : ICommandHandler<CreateUserCommand, CreateUserResult>
{
    public async Task<CreateUserResult> Handle(CreateUserCommand command, CancellationToken ct = default)
    {
        var role = Roles.Normalize(command.Role);
        if (role is null)
            return new ValidationError($"Неизвестная роль «{command.Role}»: допустимы {string.Join(", ", Roles.All)}");

        var created = await users.CreateAsync(command.Email.Trim(), command.Password, role, ct);
        return created.Match<CreateUserResult>(user => user, error => error);
    }
}