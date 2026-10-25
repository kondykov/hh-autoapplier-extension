using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;
using OneOf.Types;

namespace HhAutoApplier.Application.Abstractions;

// Учётные записи и вход. Реализация — ASP.NET Core Identity в Infrastructure; правила (последний админ, блокировка себя) —
// в обработчиках команд, здесь только хранение, пароли и cookie входа
public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default);

    Task<UserDto?> FindAsync(Guid id, CancellationToken ct = default);

    // Незаблокированные администраторы
    Task<int> CountActiveAdminsAsync(CancellationToken ct = default);

    // Ошибки политики паролей и занятый email — ValidationError
    Task<OneOf<UserDto, ValidationError>> CreateAsync(string email, string password, string role, CancellationToken ct = default);

    Task<UserDto> SetRoleAsync(Guid id, string role, CancellationToken ct = default);

    // Блокировка закрывает вход и обрывает открытые сессии (cookie перестают проходить проверку)
    Task<UserDto> SetBlockedAsync(Guid id, bool blocked, CancellationToken ct = default);

    // Пароль, заданный администратором; прежние сессии пользователя обрываются
    Task<OneOf<Success, ValidationError>> ResetPasswordAsync(Guid id, string password, CancellationToken ct = default);

    Task<OneOf<Success, ValidationError>> ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken ct = default);

    // Проверка пароля с блокировкой после серии неудач и выдача cookie входа
    Task<OneOf<UserDto, UnauthorizedError>> SignInAsync(string email, string password, CancellationToken ct = default);

    Task SignOutAsync();
}