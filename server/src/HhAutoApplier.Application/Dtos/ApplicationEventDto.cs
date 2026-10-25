using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Dtos;

public record ApplicationEventDto(DateTimeOffset At, VacancyStatus? From, VacancyStatus To, string? Reason);