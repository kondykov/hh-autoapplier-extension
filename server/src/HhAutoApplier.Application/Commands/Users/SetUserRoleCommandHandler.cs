using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Users;

namespace HhAutoApplier.Application.Commands.Users;

public class SetUserRoleCommandHandler(IUserService users) : ICommandHandler<SetUserRoleCommand, SetUserRoleResult>
{
    public async Task<SetUserRoleResult> Handle(SetUserRoleCommand command, CancellationToken ct = default)
    {
        var role = Roles.Normalize(command.Role);
        if (role is null)
            return new ValidationError($"Неизвестная роль «{command.Role}»: допустимы {string.Join(", ", Roles.All)}");

        var user = await users.FindAsync(command.UserId, ct);
        if (user is null)
            return new NotFoundError($"Пользователь {command.UserId} не найден");

        if (user.Role == role)
            return user;

        // Без администратора некому управлять пользователями. Проверка без блокировки строк: два админа, одновременно понижающие
        // друг друга, могут оставить ноль — для личного инструмента принято, выход — ADMIN_EMAIL при старте или ключ API
        if (user.Role == Roles.Admin && !user.Blocked && await users.CountActiveAdminsAsync(ct) <= 1)
            return new ConflictError("Нельзя снять роль с последнего администратора");

        return await users.SetRoleAsync(user.Id, role, ct);
    }
}