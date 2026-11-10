using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class ResolveLetterRequestsCommandHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    TimeProvider clock,
    ICurrentUser current
) : ICommandHandler<ResolveLetterRequestsCommand, ResolveLetterRequestsResult>
{
    public async Task<ResolveLetterRequestsResult> Handle(ResolveLetterRequestsCommand command, CancellationToken ct = default)
    {
        var application = await db.FindApplicationAsync(current.Id, command.VacancyId, ct);
        if (application is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == current.Id, ct);
        var resume = application.ResumeProfile is { } name
            ? await db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == current.Id && r.Name == name, ct)
            : null;
        try
        {
            application.ResolveLetterRequests(command.ConfirmCodeWords, profile, resume?.LetterTemplate, clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(application);
    }
}
