using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Vacancies;

public class ImportVacanciesCommandHandler(
    IAppDbContext db,
    VacancyEvaluator evaluator,
    ILlmClient llm,
    TimeProvider clock
) : ICommandHandler<ImportVacanciesCommand, ImportVacanciesResult>
{
    public async Task<ImportVacanciesResult> Handle(ImportVacanciesCommand command, CancellationToken ct = default)
    {
        var search = await db.Searches.AsNoTracking().FirstOrDefaultAsync(s => s.Name == command.Search, ct);
        if (search is null)
            return new ValidationError($"поиск «{command.Search}» не найден");

        var resumes = await db.Resumes.AsNoTracking().ToListAsync(ct);
        var ids = command.Vacancies.Select(v => v.Id).Distinct().ToList();
        var known = (await db.Vacancies.Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct)).ToHashSet();

        var now = clock.GetUtcNow();
        int added = 0, queued = 0;
        foreach (var item in command.Vacancies.DistinctBy(v => v.Id).Where(v => !known.Contains(v.Id)))
        {
            var vacancy = Vacancy.Create(item.Id, item.Title, item.Url ?? $"https://hh.ru/vacancy/{item.Id}", search.Name, now);
            vacancy.UpdateCard(item.Company, item.Area, Salary.From(item.SalaryFrom, item.SalaryTo, item.SalaryCurrency),
                item.WorkFormat, item.HasTest, item.PublishedAt?.ToUniversalTime());
            vacancy.UpdateDetails(null, item.Description ?? "", item.Stack ?? []);
            var application = JobApplication.Create(vacancy, null, now);
            application.Evaluate(evaluator, search.Rules, resumes, now, awaitLlm: llm.IsConfigured);

            db.Vacancies.Add(vacancy);
            db.JobApplications.Add(application);
            added++;
            if (application.Status is VacancyStatus.Queued or VacancyStatus.Pending)
                queued++;
        }

        await db.SaveChangesAsync(ct);
        return new ImportResultDto(command.Vacancies.Count, added, queued, added - queued);
    }
}
