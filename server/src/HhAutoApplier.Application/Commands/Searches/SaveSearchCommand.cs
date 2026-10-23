using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Searches;
using OneOf;

namespace HhAutoApplier.Application.Commands.Searches;

// Создать поиск или обновить существующий с тем же именем
public record SaveSearchCommand
{
    [Required]
    [MinLength(1)]
    public required string Name { get; init; }

    [Required]
    [MinLength(1)]
    public required string Query { get; init; }

    public string? Area { get; init; }

    public string? ExtraParams { get; init; }

    public bool Enabled { get; init; } = true;

    public FilterRules? Rules { get; init; }
}

// На .NET 11: public union SaveSearchResult(SearchDto, ValidationError);
[GenerateOneOf]
public partial class SaveSearchResult : OneOfBase<SearchDto, ValidationError>;
