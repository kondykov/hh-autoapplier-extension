using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Users;

namespace HhAutoApplier.Application.Commands.Users;

public class SetUserBlockedCommandHandler(IUserService users, ICurrentUser current) : ICommandHandler<SetUserBlockedCommand, SetUserBlockedResult>
{
    public async Task<SetUserBlockedResult> Handle(SetUserBlockedCommand command, CancellationToken ct = default)
    {
        var user = await users.FindAsync(command.UserId, ct);
        if (user is null)
            return new NotFoundError($"Пользователь {command.UserId} не найден");

        if (user.Blocked == command.Blocked)
            return user;

        if (command.Blocked && user.Id == current.Id)
            return new ConflictError("Нельзя заблокировать себя");

        if (command.Blocked && user.Role == Roles.Admin && await users.CountActiveAdminsAsync(ct) <= 1)
            return new ConflictError("Нельзя заблокировать последнего администратора");

        return await users.SetBlockedAsync(user.Id, command.Blocked, ct);
    }
}