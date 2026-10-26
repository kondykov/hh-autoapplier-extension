using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using Microsoft.EntityFrameworkCore;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Tokens;

public class RevokeApiTokenCommandHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock)
    : ICommandHandler<RevokeApiTokenCommand, RevokeApiTokenResult>
{
    public async Task<RevokeApiTokenResult> Handle(RevokeApiTokenCommand command, CancellationToken ct = default)
    {
        // Чужой токен — как несуществующий
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == command.Id && t.UserId == current.Id, ct);
        if (token is null)
            return new NotFoundError($"Токен {command.Id} не найден");

        try
        {
            token.Revoke(clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return new Success();
    }
}