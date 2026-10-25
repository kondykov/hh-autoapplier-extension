using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Abstractions;

public static class OwnerScope
{
    // Чьи данные читает запрос: свои; чужие (?userId) — только администратор
    public static OneOf<Guid?, ForbiddenError> Resolve(ICurrentUser current, Guid? requested)
    {
        if (requested is null || requested == current.Id)
            return current.Id;

        return current.IsAdmin ? requested : new ForbiddenError("Чужие данные доступны только администратору");
    }
}