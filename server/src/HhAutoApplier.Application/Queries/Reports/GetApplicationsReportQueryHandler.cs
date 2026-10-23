using System.Text;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Queries.Reports;

public class GetApplicationsReportQueryHandler(
    IAppDbContext db,
    IExtractor<Vacancy, VacancyDto> extractor
) : IQueryHandler<GetApplicationsReportQuery, ApplicationsReportResult>
{
    private const string Header = "Вакансия;Стек;Компания;Оплата;Формат;Ссылка;Дата;Резюме";

    public async Task<ApplicationsReportResult> Handle(GetApplicationsReportQuery query, CancellationToken ct = default)
    {
        var rows = await db.Vacancies.AsNoTracking().Where(v => v.Status == VacancyStatus.Applied)
            .OrderByDescending(v => v.AppliedAt).ToListAsync(ct);

        if (!string.Equals(query.Format, "csv", StringComparison.OrdinalIgnoreCase))
            return rows.Select(extractor.Extract).ToList();

        return new FileDto(ToCsv(rows), "text/csv; charset=utf-8", "applications.csv");
    }

    // Разделитель ';' и BOM — файл сразу открывается в Excel
    private static byte[] ToCsv(IEnumerable<Vacancy> rows)
    {
        var lines = new List<string> { Header };
        lines.AddRange(rows.Select(v => string.Join(';',
            Quote(v.Title), Quote(string.Join(", ", v.Stack)), Quote(v.Company), Quote(v.Salary?.ToString() ?? "не указана"),
            Quote(v.WorkFormat), Quote(v.Url), Quote(v.AppliedAt?.ToString("yyyy-MM-dd")), Quote(v.ResumeProfile))));

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join("\r\n", lines))];
    }

    private static string Quote(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
