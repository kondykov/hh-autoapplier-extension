using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Searches;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Searches;

public class SaveSearchCommandHandler(
    IAppDbContext db,
    IExtractor<SearchProfile, SearchDto> extractor,
    ICurrentUser current
) : ICommandHandler<SaveSearchCommand, SaveSearchResult>
{
    public async Task<SaveSearchResult> Handle(SaveSearchCommand command, CancellationToken ct = default)
    {
        var search = await db.Searches.FirstOrDefaultAsync(s => s.UserId == current.Id && s.Name == command.Name, ct);
        try
        {
            if (search is null)
            {
                search = SearchProfile.Create(command.Name, command.Query, command.Area, command.ExtraParams, command.Rules, command.Enabled, current.Id);
                db.Searches.Add(search);
            }
            else
            {
                search.Update(command.Query, command.Area, command.ExtraParams, command.Rules, command.Enabled);
            }
        }
        catch (DomainException e)
        {
            return new ValidationError(e.Message);
        }

        await db.SaveChangesAsync(ct);
        return extractor.Extract(search);
    }
}
