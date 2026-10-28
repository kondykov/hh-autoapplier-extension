using HhAutoApplier.Domain.Users;

namespace HhAutoApplier.Application.Abstractions;

// Пользователь текущего запроса; реализация — в Api по HttpContext
public interface ICurrentUser
{
    // Владелец данных запроса; null — владелец по умолчанию (пользователей ещё нет, запрос по ключу API)
    Guid? Id { get; }

    string Name { get; }

    string Role { get; }

    // Вход по персональному токену (X-Api-Key): от имени владельца, но всегда с ролью User
    bool ViaApiKey { get; }

    bool IsAdmin => Role == Roles.Admin;
}