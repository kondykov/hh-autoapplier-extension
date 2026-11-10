namespace HhAutoApplier.Application.Dtos;

// Профиль кандидата для LLM (#35): общая часть и факты по навыкам. Зарплата и готовность к тестовому (#72) — только для ответов работодателю, в LLM не идут
public record CandidateProfileDto(string? General, IReadOnlyList<SkillFactDto> Skills, int? DesiredSalaryRub = null, bool? ReadyForTestTask = null);

public record SkillFactDto(string Skill, string Facts);
