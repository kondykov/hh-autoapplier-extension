using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Candidates;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Candidates;

public class SaveCandidateProfileCommandHandler(
    IAppDbContext db,
    IExtractor<CandidateProfile, CandidateProfileDto> extractor,
    ICurrentUser current
) : ICommandHandler<SaveCandidateProfileCommand, SaveCandidateProfileResult>
{
    public async Task<SaveCandidateProfileResult> Handle(SaveCandidateProfileCommand command, CancellationToken ct = default)
    {
        var profile = await db.CandidateProfiles.FirstOrDefaultAsync(p => p.UserId == current.Id, ct);
        if (profile is null)
        {
            profile = CandidateProfile.Create(current.Id);
            db.CandidateProfiles.Add(profile);
        }

        try
        {
            profile.Update(command.General, command.Skills.Select(s => new SkillFact { Skill = s.Skill ?? "", Facts = s.Facts ?? "" }));
        }
        catch (DomainException e)
        {
            return new ValidationError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(profile);
    }
}
