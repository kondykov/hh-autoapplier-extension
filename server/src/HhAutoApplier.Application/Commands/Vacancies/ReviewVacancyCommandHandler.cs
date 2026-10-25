using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class ReviewVacancyCommandHandler(
    IAppDbContext db,
    IExtractor<Vacancy, VacancyDto> extractor,
    TimeProvider clock
) : ICommandHandler<ReviewVacancyCommand, ReviewVacancyResult>
{
    public async Task<ReviewVacancyResult> Handle(ReviewVacancyCommand command, CancellationToken ct = default)
    {
        var vacancy = await db.Vacancies.FindAsync([command.VacancyId], ct);
        if (vacancy is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        try
        {
            if (command.Approve)
                vacancy.Approve(command.CoverLetter, clock.GetUtcNow());
            else
                vacancy.Decline(command.Reason);
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(vacancy);
    }
}
