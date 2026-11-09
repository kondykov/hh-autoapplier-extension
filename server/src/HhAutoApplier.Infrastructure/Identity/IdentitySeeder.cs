using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Users;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

        await EnsureFirstAdminAsync(services);
        await AdoptOrphansAsync(services, ct);
    }

    private static async Task EnsureFirstAdminAsync(IServiceProvider services)
    {
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

    // Данные владельца по умолчанию (созданы, пока пользователей не было) переходят первому администратору из ADMIN_EMAIL —
    // явно заданному, а не «самому раннему»: после его блокировки они не уйдут к другому администратору.
    // Совпадающее с уже своим — поиск или резюме с тем же именем, отклик на ту же вакансию — остаётся у владельца по умолчанию
    private static async Task AdoptOrphansAsync(IServiceProvider services, CancellationToken ct)
    {
        var email = services.GetRequiredService<IOptions<AdminOptions>>().Value.Email;
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var admin = string.IsNullOrWhiteSpace(email) ? null : await users.FindByEmailAsync(email.Trim());
        if (admin is null || admin.Blocked || !await users.IsInRoleAsync(admin, Roles.Admin))
            return;

        var db = services.GetRequiredService<AppDbContext>();
        Guid? owner = admin.Id;
        await db.Searches.Where(s => s.UserId == null && !db.Searches.Any(o => o.UserId == owner && o.Name == s.Name))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.UserId, owner), ct);
        await db.Resumes.Where(r => r.UserId == null && !db.Resumes.Any(o => o.UserId == owner && o.Name == r.Name))
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.UserId, owner), ct);
        await db.CandidateProfiles.Where(p => p.UserId == null && !db.CandidateProfiles.Any(o => o.UserId == owner))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.UserId, owner), ct);
        await db.JobApplications.Where(a => a.UserId == null && !db.JobApplications.Any(o => o.UserId == owner && o.VacancyId == a.VacancyId))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.UserId, owner), ct);

        // Перенесённый отклик, чей поиск остался у владельца по умолчанию (совпало имя), — к одноимённому поиску администратора:
        // правила и фильтр очереди по имени — его собственные, а не ничейного поиска
        // (подзапросы, а не навигация Search: ExecuteUpdate не переводит join в SQL)
        await db.JobApplications.Where(a => a.UserId == owner && db.Searches.Any(s => s.Id == a.SearchId && s.UserId == null))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.SearchId, a => db.Searches
                .Where(s => s.UserId == owner && s.Name == db.Searches.Where(o => o.Id == a.SearchId).Select(o => o.Name).FirstOrDefault())
                .Select(s => (int?)s.Id).FirstOrDefault()), ct);
    }
}