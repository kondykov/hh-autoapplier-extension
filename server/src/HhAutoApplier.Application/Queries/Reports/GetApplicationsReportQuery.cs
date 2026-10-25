using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Queries.Reports;

// Отчёт по отправленным откликам: json или csv
public record GetApplicationsReportQuery
{
    public string? Format { get; init; }

    // Администратор — отчёт этого пользователя; без него — свой
    public Guid? UserId { get; init; }
}

// На .NET 11: public union ApplicationsReportResult(List<VacancyDto>, FileDto, ForbiddenError);
[GenerateOneOf]
public partial class ApplicationsReportResult : OneOfBase<List<VacancyDto>, FileDto, ForbiddenError>;
