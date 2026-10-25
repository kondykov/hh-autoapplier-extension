namespace HhAutoApplier.Application.Abstractions;

// Пользователь текущего запроса; реализация — в Api по HttpContext
public interface ICurrentUser
{
    // null — запрос по общему ключу API: права Admin без учётной записи
    Guid? Id { get; }

    string Name { get; }

    string Role { get; }
}