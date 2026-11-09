using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class ReevaluateVacancyCommandHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    VacancyEvaluator evaluator,
    ILlmClient llm,
    TimeProvider clock,
    ICurrentUser current
) : ICommandHandler<ReevaluateVacancyCommand, ReevaluateVacancyResult>
{
    public async Task<ReevaluateVacancyResult> Handle(ReevaluateVacancyCommand command, CancellationToken ct = default)
    {
        var application = await db.FindApplicationAsync(current.Id, command.VacancyId, ct);
        if (application is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        // В том числе для импортной карточки: без LLM после подтверждения она ушла бы в очередь мимо оценки
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: оценить заново нельзя, задайте LLM_API_KEY");

        var resumes = await db.Resumes.AsNoTracking().Where(r => r.UserId == current.Id).ToListAsync(ct);
        try
        {
            application.Reevaluate(evaluator, application.Rules, resumes, clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(application);
    }
}
