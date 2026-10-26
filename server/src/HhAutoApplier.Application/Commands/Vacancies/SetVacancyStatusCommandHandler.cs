using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class SetVacancyStatusCommandHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    TimeProvider clock,
    ICurrentUser current
) : ICommandHandler<SetVacancyStatusCommand, SetVacancyStatusResult>
{
    public async Task<SetVacancyStatusResult> Handle(SetVacancyStatusCommand command, CancellationToken ct = default)
    {
        var application = await db.FindApplicationAsync(current.Id, command.VacancyId, ct);
        if (application is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        var now = clock.GetUtcNow();
        try
        {
            switch (command.Status)
            {
                case VacancyStatus.Applied:
                    application.MarkApplied(command.Resume, now);
                    break;
                case VacancyStatus.Skipped:
                    application.Skip(command.Reason, now);
                    break;
                case VacancyStatus.Failed:
                    application.Fail(command.Reason, now);
                    break;
                case VacancyStatus.Rejected:
                    application.Reject(command.Reason, now);
                    break;
                case VacancyStatus.Queued:
                    application.Requeue(now);
                    break;
                default:
                    return new ConflictError($"Статус {command.Status} нельзя выставить вручную");
            }
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(application);
    }
}