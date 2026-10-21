using HhAutoApplier.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();
    public DbSet<SearchProfile> Searches => Set<SearchProfile>();
    public DbSet<ResumeProfile> Resumes => Set<ResumeProfile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Vacancy>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.Status, x.Score });
            e.HasIndex(x => x.SearchName);
        });
        b.Entity<SearchProfile>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.OwnsOne(x => x.Rules, o => o.ToJson());
        });
        b.Entity<ResumeProfile>(e => e.HasIndex(x => x.Name).IsUnique());
    }
}
