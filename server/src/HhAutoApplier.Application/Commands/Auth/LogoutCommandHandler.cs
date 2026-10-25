using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Auth;

public class LogoutCommandHandler(IUserService users) : ICommandHandler<LogoutCommand, Success>
{
    public async Task<Success> Handle(LogoutCommand command, CancellationToken ct = default)
    {
        await users.SignOutAsync();
        return new Success();
    }
}