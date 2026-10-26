namespace HhAutoApplier.Infrastructure.Identity;

// Первый администратор (ADMIN_EMAIL, ADMIN_PASSWORD в .env): создаётся при старте, если администраторов ещё нет
public class AdminOptions
{
    public const string Section = "Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }
}