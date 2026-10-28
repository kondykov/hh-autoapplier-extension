using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> builder)
    {
        builder.ToTable("CandidateProfiles");
        // Один профиль у пользователя; у владельца по умолчанию (UserId = null) — тоже один
        builder.HasIndex(x => x.UserId).IsUnique().AreNullsDistinct(false);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.General).HasMaxLength(CandidateProfile.MaxGeneral);
        builder.Ignore(x => x.IsEmpty);
        // Факты по навыкам — колонкой jsonb
        builder.OwnsMany(x => x.Skills, o => o.ToJson());
    }
}
