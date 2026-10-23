using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Resumes;

namespace HhAutoApplier.Application.Extractors;

public class ResumeExtractor : IExtractor<ResumeProfile, ResumeDto>
{
    public ResumeDto Extract(ResumeProfile source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Match = source.Match,
        Keywords = source.Keywords,
    };
}
