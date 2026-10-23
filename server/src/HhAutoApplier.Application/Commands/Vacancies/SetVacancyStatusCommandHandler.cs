using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class SetVacancyStatusCommandHandler(
    IAppDbContext db,
    IExtractor<Vacancy, VacancyDto> extractor,
    TimeProvider clock
) : ICommandHandler<SetVacancyStatusCommand, SetVacancyStatusResult>
{
    public async Task<SetVacancyStatusResult> Handle(SetVacancyStatusCommand command, CancellationToken ct = default)
    {
        var vacancy = await db.Vacancies.FindAsync([command.VacancyId], ct);
        if (vacancy is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        try
        {
            switch (command.Status)
            {
                case VacancyStatus.Applied:
                    vacancy.MarkApplied(command.Resume, clock.GetUtcNow());
                    break;
                case VacancyStatus.Skipped:
                    vacancy.Skip(command.Reason);
                    break;
                case VacancyStatus.Failed:
                    vacancy.Fail(command.Reason);
                    break;
                case VacancyStatus.Rejected:
                    vacancy.Reject(command.Reason);
                    break;
                case VacancyStatus.Queued:
                    vacancy.Requeue();
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
        return extractor.Extract(vacancy);
    }
}
