using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class SubmitQuestionnaireCommandHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    TimeProvider clock
) : ICommandHandler<SubmitQuestionnaireCommand, SubmitQuestionnaireResult>
{
    public async Task<SubmitQuestionnaireResult> Handle(SubmitQuestionnaireCommand command, CancellationToken ct = default)
    {
        var application = await db.FindApplicationAsync(command.VacancyId, ct);
        if (application is null)
            return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        List<VacancyQuestion> questions;
        try
        {
            questions = command.Questions.Select(q => VacancyQuestion.Create(q.Question, q.Type, q.Options)).ToList();
        }
        catch (DomainException e)
        {
            return new ValidationError(e.Message);
        }

        try
        {
            application.RequireAnswers(questions, clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return new ConflictError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(application);
    }
}