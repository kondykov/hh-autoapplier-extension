using HhAutoApplier.Application.Dtos;
using OneOf;

namespace HhAutoApplier.Application.Queries.Reports;

// Отчёт по отправленным откликам: json или csv
public record GetApplicationsReportQuery
{
    public string? Format { get; init; }
}

// На .NET 11: public union ApplicationsReportResult(List<VacancyDto>, FileDto);
[GenerateOneOf]
public partial class ApplicationsReportResult : OneOfBase<List<VacancyDto>, FileDto>;
