using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Vacancy> Vacancies { get; }

    DbSet<SearchProfile> Searches { get; }

    DbSet<ResumeProfile> Resumes { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
