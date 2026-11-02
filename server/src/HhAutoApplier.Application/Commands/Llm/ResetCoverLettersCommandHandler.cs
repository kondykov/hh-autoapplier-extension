using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Llm;

public class ResetCoverLettersCommandHandler(IAppDbContext db, ICurrentUser current) : ICommandHandler<ResetCoverLettersCommand, ResetCoverLettersResultDto>
{
    // Только очередь и проверка: письма отправленных откликов — история, их не трогаем. Эндпоинт — только для Admin:
    // свои письма или пользователя из ?userId (#57); поправленные человеком — только явно
    public async Task<ResetCoverLettersResultDto> Handle(ResetCoverLettersCommand command, CancellationToken ct = default)
    {
        var owner = command.UserId ?? current.Id;
        var q = db.AllApplications().Where(a => a.UserId == owner
                                                && (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter != null);
        if (!command.IncludeManual)
            q = q.Where(a => a.CoverLetterModel != JobApplication.ManualLetter);
        if (command.VacancyIds.Count > 0)
            q = q.Where(a => command.VacancyIds.Contains(a.VacancyId));

        var applications = await q.ToListAsync(ct);
        foreach (var application in applications)
            application.ResetCoverLetter();

        await db.SaveChangesAsync(ct);
        return new ResetCoverLettersResultDto(applications.Count);
    }
}
