using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();

    public DbSet<SearchProfile> Searches => Set<SearchProfile>();

    public DbSet<ResumeProfile> Resumes => Set<ResumeProfile>();

    // Маппинг каждой сущности — в своём классе в Configurations/
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
