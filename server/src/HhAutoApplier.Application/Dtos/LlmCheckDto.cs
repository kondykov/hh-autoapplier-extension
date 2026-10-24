namespace HhAutoApplier.Application.Dtos;

public record LlmCheckDto(string Model, string Answer, int LatencyMs, int Tokens);
