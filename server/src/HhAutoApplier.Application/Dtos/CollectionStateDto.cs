namespace HhAutoApplier.Application.Dtos;

// Один проход сбора: идущий или завершённый
public record CollectionRunDto
{
    public string Trigger { get; init; } = "";

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public string? Search { get; init; }

    public int Page { get; init; }

    public int Searches { get; init; }

    public int Seen { get; init; }

    public int Added { get; init; }

    public int Queued { get; init; }

    public int Skipped { get; init; }

    public string? Error { get; init; }
}

// Current — идущий проход, null — сбор сейчас не идёт. Last — последний завершённый с момента старта сервера
public record CollectionStateDto(CollectionRunDto? Current, CollectionRunDto? Last);
