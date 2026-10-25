using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Users;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OneOf;
using OneOf.Types;

namespace HhAutoApplier.Infrastructure.Identity;

public class UserService(
    AppDbContext db,
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    TimeProvider clock
) : IUserService
{
    private const string WrongCredentials = "Неверный email или пароль";

    // Хэш случайного пароля в формате Identity v3: проверка по нему стоит столько же, сколько по настоящему
    private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString());

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        var roles = (await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                select new { userRole.UserId, role.Name }
            ).ToListAsync(ct))
            .ToLookup(r => r.UserId, r => r.Name);

        var all = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);
        return all.Select(u => ToDto(u, roles[u.Id].FirstOrDefault())).ToList();
    }

    public async Task<UserDto?> FindAsync(Guid id, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is null ? null : await ToDtoAsync(user);
    }

    public async Task<UserDto?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email);
        return user is null ? null : await ToDtoAsync(user);
    }

    public Task<bool> AnyAsync(CancellationToken ct = default) => db.Users.AnyAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetBlockedIdsAsync(CancellationToken ct = default) =>
        await db.Users.Where(u => u.Blocked).Select(u => u.Id).ToListAsync(ct);

    public async Task<int> CountActiveAdminsAsync(CancellationToken ct = default) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => !u.Blocked);

    // Пользователь и роль — в одной транзакции: каждый вызов UserManager сохраняет отдельно
    public async Task<OneOf<UserDto, ValidationError>> CreateAsync(string email, string password, string role, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = new AppUser { UserName = email, Email = email, CreatedAt = clock.GetUtcNow() };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            return new ValidationError(Describe(created));

        Ensure(await users.AddToRoleAsync(user, role));
        await transaction.CommitAsync(ct);
        return ToDto(user, role);
    }

    // Новая роль попадёт в cookie при следующем входе: смена штампа безопасности обрывает текущие сессии
    public async Task<UserDto> SetRoleAsync(Guid id, string role, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await GetAsync(id);
        Ensure(await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user)));
        Ensure(await users.AddToRoleAsync(user, role));
        Ensure(await users.UpdateSecurityStampAsync(user));
        await transaction.CommitAsync(ct);
        return ToDto(user, role);
    }

    public async Task<UserDto> SetBlockedAsync(Guid id, bool blocked, CancellationToken ct = default)
    {
        var user = await GetAsync(id);
        user.Blocked = blocked;
        Ensure(await users.UpdateSecurityStampAsync(user));
        return await ToDtoAsync(user);
    }

    public async Task<OneOf<Success, ValidationError>> ResetPasswordAsync(Guid id, string password, CancellationToken ct = default)
    {
        var user = await GetAsync(id);

        // Сначала проверить пароль политикой: снять старый и не поставить новый нельзя
        foreach (var validator in users.PasswordValidators)
        {
            var valid = await validator.ValidateAsync(users, user, password);
            if (!valid.Succeeded)
                return new ValidationError(Describe(valid));
        }

        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        Ensure(await users.UpdateSecurityStampAsync(user));
        return new Success();
    }

    // Смена пароля меняет штамп безопасности; текущая сессия перевыпускается, остальные обрываются.
    // Текущий пароль проверяется с подсчётом неудач: по украденной сессии его не подобрать.
    // Заблокированному отказ: иначе перевыпуск cookie продлил бы сессию после блокировки
    public async Task<OneOf<Success, ValidationError>> ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await GetAsync(id);
        var check = await signIn.CheckPasswordSignInAsync(user, currentPassword, lockoutOnFailure: true);
        if (check.IsNotAllowed)
            return new ValidationError("Пользователь заблокирован");

        if (check.IsLockedOut)
            return new ValidationError("Слишком много неудачных попыток, попробуйте позже");

        if (!check.Succeeded)
            return new ValidationError("Неверный текущий пароль");

        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
            return new ValidationError(Describe(changed));

        await signIn.RefreshSignInAsync(user);
        return new Success();
    }

    public async Task<OneOf<UserDto, UnauthorizedError>> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            // Хэш считается и для неизвестного email: по времени ответа не понять, заведён ли он
            users.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyHash, password);
            return new UnauthorizedError(WrongCredentials);
        }

        var check = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return new UnauthorizedError("Слишком много неудачных попыток входа, попробуйте позже");

        // Заблокированному Identity отказывает до проверки пароля; о блокировке — только при верном пароле,
        // чтобы не подсказывать, какие email заведены
        if (check.IsNotAllowed)
            return new UnauthorizedError(await users.CheckPasswordAsync(user, password) ? "Пользователь заблокирован" : WrongCredentials);

        if (!check.Succeeded)
            return new UnauthorizedError(WrongCredentials);

        await signIn.SignInAsync(user, isPersistent: true);
        return await ToDtoAsync(user);
    }

    public Task SignOutAsync() => signIn.SignOutAsync();

    public static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(e => e.Description));

    // Шаг, который при верных данных не падает; отказ (например, параллельная правка того же пользователя) — ошибка, а не тихий успех
    public static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(Describe(result));
    }

    private async Task<AppUser> GetAsync(Guid id) =>
        await users.FindByIdAsync(id.ToString()) ?? throw new InvalidOperationException($"Пользователь {id} не найден");

    private async Task<UserDto> ToDtoAsync(AppUser user) => ToDto(user, (await users.GetRolesAsync(user)).FirstOrDefault());

    private static UserDto ToDto(AppUser user, string? role) => new()
    {
        Id = user.Id,
        Email = user.Email ?? user.UserName ?? "",
        Role = role ?? "",
        Blocked = user.Blocked,
        CreatedAt = user.CreatedAt,
    };
}