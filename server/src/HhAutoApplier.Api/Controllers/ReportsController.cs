using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Queries.Reports;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Отчёты")]
[Route("api/report")]
public class ReportsController : ApiController
{
    /// <summary>
    /// Отчёт по отправленным откликам: JSON или CSV (format=csv, открывается в Excel).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<VacancyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] GetApplicationsReportQuery query,
        [FromServices] GetApplicationsReportQueryHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(query, ct)).Value);
    }
}
