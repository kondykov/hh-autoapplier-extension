using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Candidates;

namespace HhAutoApplier.Application.Extractors;

public class CandidateProfileExtractor : IExtractor<CandidateProfile, CandidateProfileDto>
{
    public CandidateProfileDto Extract(CandidateProfile source) =>
        new(source.General, source.Skills.Select(s => new SkillFactDto(s.Skill, s.Facts)).ToList());
}
