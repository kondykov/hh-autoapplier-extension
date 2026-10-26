namespace HhAutoApplier.Application.Dtos;

public record UserDto
{
    public Guid Id { get; init; }

    public required string Email { get; init; }

    public required string Role { get; init; }

    public bool Blocked { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}