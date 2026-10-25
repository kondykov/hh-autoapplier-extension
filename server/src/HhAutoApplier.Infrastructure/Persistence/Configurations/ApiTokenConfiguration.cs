using HhAutoApplier.Domain.Users;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Hash).HasMaxLength(64);
        builder.Property(x => x.Prefix).HasMaxLength(16);
        builder.Property(x => x.Name).HasMaxLength(100);
        // Поиск по хэшу на каждый запрос расширения
        builder.HasIndex(x => x.Hash).IsUnique();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(x => x.Active);
    }
}