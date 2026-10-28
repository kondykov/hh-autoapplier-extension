namespace HhAutoApplier.Application.Dtos;

// Профиль кандидата для LLM (#35): общая часть и факты по навыкам
public record CandidateProfileDto(string? General, IReadOnlyList<SkillFactDto> Skills);

public record SkillFactDto(string Skill, string Facts);
