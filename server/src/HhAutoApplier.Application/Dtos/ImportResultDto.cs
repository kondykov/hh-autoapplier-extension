namespace HhAutoApplier.Application.Dtos;

// Waiting — отклики на чужую неподтверждённую карточку из импорта: ждут подтверждения источником при сборе
public record ImportResultDto(int Received, int Added, int Queued, int Skipped, int Waiting);
