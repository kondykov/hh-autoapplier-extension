using HhAutoApplier.Domain.Searches;

namespace HhAutoApplier.Application.Dtos;

public record SearchDto
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Query { get; init; }

    public string? Area { get; init; }

    public string? ExtraParams { get; init; }

    public bool Enabled { get; init; }

    public required FilterRules Rules { get; init; }
}
