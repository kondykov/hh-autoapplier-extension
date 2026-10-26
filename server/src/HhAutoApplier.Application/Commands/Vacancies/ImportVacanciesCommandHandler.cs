using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Vacancies;

// Импорт в свой поиск: вакансия общая — новая создаётся, известная берётся из базы; отклик — свой, если его ещё нет
public class ImportVacanciesCommandHandler(
    IAppDbContext db,
    VacancyEvaluator evaluator,
    ILlmClient llm,
    TimeProvider clock,
    ICurrentUser current
) : ICommandHandler<ImportVacanciesCommand, ImportVacanciesResult>
{
    public async Task<ImportVacanciesResult> Handle(ImportVacanciesCommand command, CancellationToken ct = default)
    {
        var search = await db.Searches.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == current.Id && s.Name == command.Search, ct);
        if (search is null)
            return new ValidationError($"поиск «{command.Search}» не найден");

        var resumes = await db.Resumes.AsNoTracking().Where(r => r.UserId == current.Id).ToListAsync(ct);
        var ids = command.Vacancies.Select(v => v.Id).Distinct().ToList();
        var known = await db.Vacancies.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var applied = (await db.JobApplications.Where(a => a.UserId == current.Id && ids.Contains(a.VacancyId))
            .Select(a => a.VacancyId).ToListAsync(ct)).ToHashSet();

        var now = clock.GetUtcNow();
        int added = 0, queued = 0, waiting = 0;
        foreach (var item in command.Vacancies.DistinctBy(v => v.Id).Where(v => !applied.Contains(v.Id)))
        {
            if (known.TryGetValue(item.Id, out var vacancy) && vacancy.Imported)
            {
                // Карточку создал импорт другого пользователя, источник её ещё не подтвердил: оценивать по ней — пускать
                // чужие данные в свои правила и промпты. Отклик ждёт в New, сбор подтвердит карточку и оценит его
                db.JobApplications.Add(JobApplication.Create(vacancy, current.Id, now, search.Id));
                added++;
                waiting++;
                continue;
            }

            if (vacancy is null)
            {
                vacancy = Vacancy.Create(item.Id, item.Title, item.Url ?? "", search.Name, now, imported: true);
                vacancy.UpdateCard(item.Company, item.Area, Salary.From(item.SalaryFrom, item.SalaryTo, item.SalaryCurrency),
                    item.WorkFormat, item.HasTest, item.PublishedAt?.ToUniversalTime());
                vacancy.UpdateDetails(null, item.Description ?? "", item.Stack ?? []);
                db.Vacancies.Add(vacancy);
            }

            var application = JobApplication.Create(vacancy, current.Id, now, search.Id);
            application.Evaluate(evaluator, search.Rules, resumes, now, awaitLlm: llm.IsConfigured);
            db.JobApplications.Add(application);
            added++;
            if (application.Status is VacancyStatus.Queued or VacancyStatus.Pending)
                queued++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Сбор успел добавить ту же вакансию или отклик (первичный ключ, уникальный индекс)
            return new ConflictError("Те же вакансии одновременно добавил сбор — повторите импорт");
        }

        return new ImportResultDto(command.Vacancies.Count, added, queued, added - queued - waiting, waiting);
    }
}