using HhAutoApplier.Domain.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class LlmCallConfiguration : IEntityTypeConfiguration<LlmCall>
{
    public void Configure(EntityTypeBuilder<LlmCall> builder)
    {
        builder.ToTable("LlmCalls");
        builder.Property(x => x.Purpose).HasMaxLength(32);
        builder.Property(x => x.Model).HasMaxLength(128);
        // Дневной лимит считается по времени запросов
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.VacancyId);
    }
}
