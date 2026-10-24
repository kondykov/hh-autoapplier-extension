namespace HhAutoApplier.Application.Dtos;

public record ResumeDto
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Match { get; init; }

    public required string[] Keywords { get; init; }

    public string? Summary { get; init; }
}
