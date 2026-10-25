using HhAutoApplier.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Identity;

public static class IdentitySeeder
{
    // Роли — всегда; первый администратор — из конфига, только пока администраторов нет
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                UserService.Ensure(await roles.CreateAsync(new IdentityRole<Guid>(role)));
        }

        var admin = services.GetRequiredService<IOptions<AdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(admin.Email) || string.IsNullOrWhiteSpace(admin.Password))
            return;

        var users = services.GetRequiredService<UserManager<AppUser>>();
        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
            return;

        // Учётка с этим email уже есть (прошлый запуск упал до выдачи роли или администраторов не осталось) — вернуть ей
        // роль Admin; пароль не трогаем
        var email = admin.Email.Trim();
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new AppUser { UserName = email, Email = email, CreatedAt = services.GetRequiredService<TimeProvider>().GetUtcNow() };
            var created = await users.CreateAsync(user, admin.Password);
            if (!created.Succeeded)
                throw new InvalidOperationException($"Первый администратор не создан: {UserService.Describe(created)}");
        }

        UserService.Ensure(await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user)));
        UserService.Ensure(await users.AddToRoleAsync(user, Roles.Admin));
        user.Blocked = false;
        UserService.Ensure(await users.UpdateSecurityStampAsync(user));
    }
}