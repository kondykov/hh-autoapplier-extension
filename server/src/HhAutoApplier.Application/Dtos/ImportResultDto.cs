namespace HhAutoApplier.Application.Dtos;

public record ImportResultDto(int Received, int Added, int Queued, int Skipped);
