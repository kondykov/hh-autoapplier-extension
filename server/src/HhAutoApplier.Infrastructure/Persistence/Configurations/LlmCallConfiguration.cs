using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Infrastructure.Identity;
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
        // Неудачи писем — по отклику (#57). Удалённый пользователь не стирает журнал: на нём держится дневной лимит
        builder.HasIndex(x => new { x.UserId, x.VacancyId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
