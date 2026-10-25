using HhAutoApplier.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class ResumeProfileConfiguration : IEntityTypeConfiguration<ResumeProfile>
{
    public void Configure(EntityTypeBuilder<ResumeProfile> builder)
    {
        builder.ToTable("Resumes");
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
