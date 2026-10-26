namespace HhAutoApplier.Application.Dtos;

public record ApiTokenDto(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

// Открытое значение токена отдаётся один раз — при создании
public record CreatedApiTokenDto(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, string Value);