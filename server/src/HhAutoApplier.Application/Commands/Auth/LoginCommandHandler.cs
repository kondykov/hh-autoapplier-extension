using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;

namespace HhAutoApplier.Application.Commands.Auth;

public class LoginCommandHandler(IUserService users) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand command, CancellationToken ct = default)
    {
        var signedIn = await users.SignInAsync(command.Email.Trim(), command.Password, ct);
        return signedIn.Match<LoginResult>(user => user, error => error);
    }
}