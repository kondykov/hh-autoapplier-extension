namespace HhAutoApplier.Application.Dtos;

// Кто выполняет запрос. Id = null — владелец по умолчанию (пользователей ещё нет, вход по ключу API)
public record CurrentUserDto
{
    public Guid? Id { get; init; }

    public required string Name { get; init; }

    public required string Role { get; init; }

    // Вход по персональному токену: роль всегда User
    public bool ViaApiKey { get; init; }
}