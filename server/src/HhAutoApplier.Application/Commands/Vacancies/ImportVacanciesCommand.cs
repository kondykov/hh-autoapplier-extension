using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Vacancies;

// Приём вакансий от клиента (например, расширения) — запасной путь, если сборщик не может достучаться до источника
public record ImportVacanciesCommand
{
    [Required]
    public required string Search { get; init; }

    [Required]
    public required List<ImportedVacancy> Vacancies { get; init; }
}

public record ImportedVacancy
{
    [Required]
    public required string Id { get; init; }

    [Required]
    public required string Title { get; init; }

    public string? Company { get; init; }

    public string? Url { get; init; }

    public string? Area { get; init; }

    public string? Description { get; init; }

    public int? SalaryFrom { get; init; }

    public int? SalaryTo { get; init; }

    public string? SalaryCurrency { get; init; }

    public string? WorkFormat { get; init; }

    public string[]? Stack { get; init; }

    public bool HasTest { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}

// На .NET 11: public union ImportVacanciesResult(ImportResultDto, ValidationError);
[GenerateOneOf]
public partial class ImportVacanciesResult : OneOfBase<ImportResultDto, ValidationError>;
