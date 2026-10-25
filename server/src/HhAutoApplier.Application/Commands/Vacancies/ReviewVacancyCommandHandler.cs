using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class ReviewVacancyCommandHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    TimeProvider clock
) : ICommandHandler<ReviewVacancyCommand, ReviewVacancyResult>
{
    public async Task<ReviewVacancyResult> Handle(ReviewVacancyCommand command, CancellationToken ct = default)
    {
        var application = await db.FindApplicationAsync(command.VacancyId, ct);
        if (application is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        try
        {
            if (command.Approve)
                application.Approve(command.CoverLetter, clock.GetUtcNow());
            else
                application.Decline(command.Reason, clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(application);
    }
}