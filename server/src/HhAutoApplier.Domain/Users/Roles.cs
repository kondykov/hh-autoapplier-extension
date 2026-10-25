namespace HhAutoApplier.Domain.Users;

// Роли пользователей: Admin — пользователи и общие настройки, User — только своё. У пользователя ровно одна роль
public static class Roles
{
    public const string Admin = "Admin";

    public const string User = "User";

    public static readonly string[] All = [Admin, User];

    // Имя роли без учёта регистра → каноническое; неизвестная — null
    public static string? Normalize(string? role) =>
        All.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));
}