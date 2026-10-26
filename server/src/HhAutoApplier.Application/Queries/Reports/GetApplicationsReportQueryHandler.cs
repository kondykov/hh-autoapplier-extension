using System.Text;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Reports;

public class GetApplicationsReportQueryHandler(
    IAppDbContext db,
    IExtractor<JobApplication, VacancyDto> extractor,
    ICurrentUser current
) : IQueryHandler<GetApplicationsReportQuery, ApplicationsReportResult>
{
    private const string Header = "Вакансия;Стек;Компания;Оплата;Формат;Ссылка;Дата;Резюме";

    public async Task<ApplicationsReportResult> Handle(GetApplicationsReportQuery query, CancellationToken ct = default)
    {
        var scope = OwnerScope.Resolve(current, query.UserId);
        if (scope.IsT1)
            return scope.AsT1;

        var rows = await db.Applications(scope.AsT0).AsNoTracking().Where(a => a.Status == VacancyStatus.Applied)
            .OrderByDescending(a => a.AppliedAt).ToListAsync(ct);

        if (!string.Equals(query.Format, "csv", StringComparison.OrdinalIgnoreCase))
            return rows.Select(extractor.Extract).ToList();

        return new FileDto(ToCsv(rows), "text/csv; charset=utf-8", "applications.csv");
    }

    // Разделитель ';' и BOM — файл сразу открывается в Excel
    private static byte[] ToCsv(IEnumerable<JobApplication> rows)
    {
        var lines = new List<string> { Header };
        lines.AddRange(rows.Select(a => string.Join(';',
            Quote(a.Vacancy.Title), Quote(string.Join(", ", a.Vacancy.Stack)), Quote(a.Vacancy.Company), Quote(a.Vacancy.Salary?.ToString() ?? "не указана"),
            Quote(a.Vacancy.WorkFormat), Quote(a.Vacancy.Url), Quote(a.AppliedAt?.ToString("yyyy-MM-dd")), Quote(a.ResumeProfile))));

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join("\r\n", lines))];
    }

    private static string Quote(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
