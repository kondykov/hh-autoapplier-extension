using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;

namespace HhAutoApplier.Application.Commands.Auth;

public class ChangePasswordCommandHandler(IUserService users, ICurrentUser current) : ICommandHandler<ChangePasswordCommand, ChangePasswordResult>
{
    public async Task<ChangePasswordResult> Handle(ChangePasswordCommand command, CancellationToken ct = default)
    {
        if (current.ViaApiKey || current.Id is not { } id)
            return new ConflictError("Вход по токену: пароль меняется после входа по паролю");

        var changed = await users.ChangePasswordAsync(id, command.CurrentPassword, command.NewPassword, ct);
        return changed.Match<ChangePasswordResult>(success => success, error => error);
    }
}