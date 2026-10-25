using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Llm;

public class ResetCoverLettersCommandHandler(IAppDbContext db) : ICommandHandler<ResetCoverLettersCommand, ResetCoverLettersResultDto>
{
    // Только очередь: письма отправленных откликов — история, их не трогаем
    public async Task<ResetCoverLettersResultDto> Handle(ResetCoverLettersCommand command, CancellationToken ct = default)
    {
        var q = db.Vacancies.Where(v => v.Status == VacancyStatus.Queued && v.CoverLetter != null);
        if (command.VacancyIds.Count > 0)
            q = q.Where(v => command.VacancyIds.Contains(v.Id));

        var vacancies = await q.ToListAsync(ct);
        foreach (var vacancy in vacancies)
            vacancy.ResetCoverLetter();

        await db.SaveChangesAsync(ct);
        return new ResetCoverLettersResultDto(vacancies.Count);
    }
}
