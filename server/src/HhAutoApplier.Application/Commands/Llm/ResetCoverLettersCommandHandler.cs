using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Llm;

public class ResetCoverLettersCommandHandler(IAppDbContext db) : ICommandHandler<ResetCoverLettersCommand, ResetCoverLettersResultDto>
{
    // Только очередь и проверка: письма отправленных откликов — история, их не трогаем
    public async Task<ResetCoverLettersResultDto> Handle(ResetCoverLettersCommand command, CancellationToken ct = default)
    {
        var q = db.Applications().Where(a => (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter != null);
        if (command.VacancyIds.Count > 0)
            q = q.Where(a => command.VacancyIds.Contains(a.VacancyId));

        var applications = await q.ToListAsync(ct);
        foreach (var application in applications)
            application.ResetCoverLetter();

        await db.SaveChangesAsync(ct);
        return new ResetCoverLettersResultDto(applications.Count);
    }
}
