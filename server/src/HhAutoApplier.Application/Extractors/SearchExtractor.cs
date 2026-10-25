using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Searches;

namespace HhAutoApplier.Application.Extractors;

public class SearchExtractor : IExtractor<SearchProfile, SearchDto>
{
    public SearchDto Extract(SearchProfile source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Query = source.Query,
        Area = source.Area,
        ExtraParams = source.ExtraParams,
        Enabled = source.Enabled,
        Rules = source.Rules,
    };
}
