using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Users;

namespace HhAutoApplier.Application.Commands.Tokens;

public class CreateApiTokenCommandHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock)
    : ICommandHandler<CreateApiTokenCommand, CreateApiTokenResult>
{
    public async Task<CreateApiTokenResult> Handle(CreateApiTokenCommand command, CancellationToken ct = default)
    {
        if (current.ViaApiKey || current.Id is not { } userId)
            return new ConflictError("Токен выпускается после входа по паролю");

        try
        {
            var (token, value) = ApiToken.Create(userId, command.Name, clock.GetUtcNow());
            db.ApiTokens.Add(token);
            await db.SaveChangesAsync(ct);
            return new CreatedApiTokenDto(token.Id, token.Name, token.Prefix, token.CreatedAt, value);
        }
        catch (DomainException e)
        {
            return new ValidationError(e.Message);
        }
    }
}