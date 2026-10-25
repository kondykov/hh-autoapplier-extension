using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;

namespace HhAutoApplier.Application.Commands.Users;

public class ResetUserPasswordCommandHandler(IUserService users) : ICommandHandler<ResetUserPasswordCommand, ResetUserPasswordResult>
{
    public async Task<ResetUserPasswordResult> Handle(ResetUserPasswordCommand command, CancellationToken ct = default)
    {
        if (await users.FindAsync(command.UserId, ct) is null)
            return new NotFoundError($"Пользователь {command.UserId} не найден");

        var reset = await users.ResetPasswordAsync(command.UserId, command.Password, ct);
        return reset.Match<ResetUserPasswordResult>(success => success, error => error);
    }
}