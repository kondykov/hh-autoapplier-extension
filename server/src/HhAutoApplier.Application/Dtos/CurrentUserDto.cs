namespace HhAutoApplier.Application.Dtos;

// Кто выполняет запрос. Id = null — вход по общему ключу API (X-Api-Key), у него нет учётной записи
public record CurrentUserDto
{
    public Guid? Id { get; init; }

    public required string Name { get; init; }

    public required string Role { get; init; }
}