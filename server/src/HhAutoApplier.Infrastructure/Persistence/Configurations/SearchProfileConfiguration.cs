using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class SearchProfileConfiguration : IEntityTypeConfiguration<SearchProfile>
{
    public void Configure(EntityTypeBuilder<SearchProfile> builder)
    {
        builder.ToTable("Searches");
        // Имя уникально у владельца; у владельца по умолчанию (UserId = null) — тоже
        builder.HasIndex(x => new { x.UserId, x.Name }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(x => x.Rules, o => o.ToJson());
    }
}