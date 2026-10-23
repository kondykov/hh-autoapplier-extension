using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Resumes;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Resumes;

public class SaveResumeCommandHandler(
    IAppDbContext db,
    IExtractor<ResumeProfile, ResumeDto> extractor
) : ICommandHandler<SaveResumeCommand, SaveResumeResult>
{
    public async Task<SaveResumeResult> Handle(SaveResumeCommand command, CancellationToken ct = default)
    {
        var resume = await db.Resumes.FirstOrDefaultAsync(r => r.Name == command.Name, ct);
        try
        {
            if (resume is null)
            {
                resume = ResumeProfile.Create(command.Name, command.Match, command.Keywords);
                db.Resumes.Add(resume);
            }
            else
            {
                resume.Update(command.Match, command.Keywords);
            }
        }
        catch (DomainException e)
        {
            return new ValidationError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(resume);
    }
}
