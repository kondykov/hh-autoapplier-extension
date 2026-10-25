using Microsoft.AspNetCore.Identity;

namespace HhAutoApplier.Infrastructure.Identity;

// Учётная запись ASP.NET Core Identity; логин — email. Роль — одна, через таблицу ролей Identity
public class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }

    // Заблокирован администратором: вход закрыт. Временная блокировка после неудачных входов — штатный LockoutEnd Identity
    public bool Blocked { get; set; }
}