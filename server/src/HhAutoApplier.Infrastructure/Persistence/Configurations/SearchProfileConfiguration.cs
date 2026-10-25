using HhAutoApplier.Domain.Searches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class SearchProfileConfiguration : IEntityTypeConfiguration<SearchProfile>
{
    public void Configure(EntityTypeBuilder<SearchProfile> builder)
    {
        builder.ToTable("Searches");
        builder.HasIndex(x => x.Name).IsUnique();
        builder.OwnsOne(x => x.Rules, o => o.ToJson());
    }
}
