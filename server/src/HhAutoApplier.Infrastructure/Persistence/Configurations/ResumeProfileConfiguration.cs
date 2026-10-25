using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class ResumeProfileConfiguration : IEntityTypeConfiguration<ResumeProfile>
{
    public void Configure(EntityTypeBuilder<ResumeProfile> builder)
    {
        builder.ToTable("Resumes");
        // Имя уникально у владельца; у владельца по умолчанию (UserId = null) — тоже
        builder.HasIndex(x => new { x.UserId, x.Name }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}