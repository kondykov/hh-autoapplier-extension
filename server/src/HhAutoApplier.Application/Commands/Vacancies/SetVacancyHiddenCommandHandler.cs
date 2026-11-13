using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class SetVacancyHiddenCommandHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    TimeProvider clock,
    ICurrentUser current
) : ICommandHandler<SetVacancyHiddenCommand, SetVacancyHiddenResult>
{
    public async Task<SetVacancyHiddenResult> Handle(SetVacancyHiddenCommand command, CancellationToken ct = default)
    {
        var application = await db.FindApplicationAsync(current.Id, command.VacancyId, ct);
        if (application is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        try
        {
            if (command.Hidden)
                application.Hide(clock.GetUtcNow());
            else
                application.Unhide();
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(application);
    }
}
