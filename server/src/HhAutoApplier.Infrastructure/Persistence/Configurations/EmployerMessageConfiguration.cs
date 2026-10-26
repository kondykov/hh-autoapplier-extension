using HhAutoApplier.Domain.Dialogs;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class EmployerMessageConfiguration : IEntityTypeConfiguration<EmployerMessage>
{
    public void Configure(EntityTypeBuilder<EmployerMessage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VacancyId).HasMaxLength(64);
        builder.Property(x => x.Title).HasMaxLength(EmployerMessage.MaxTitle);
        builder.Property(x => x.Company).HasMaxLength(EmployerMessage.MaxTitle);
        builder.Property(x => x.Text).HasMaxLength(EmployerMessage.MaxText);
        builder.Property(x => x.HhState).HasMaxLength(32);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Summary).HasMaxLength(EmployerMessage.MaxSummary);
        builder.Property(x => x.SuggestedReply).HasMaxLength(EmployerMessage.MaxText);
        builder.Property(x => x.LlmModel).HasMaxLength(100);
        builder.Property(x => x.AutoReply).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.AutoReplyError).HasMaxLength(500);
        // Одна запись на чат пользователя
        builder.HasIndex(x => new { x.UserId, x.ChatId }).IsUnique();
        // Разбор LLM выбирает неразобранные
        builder.HasIndex(x => x.Kind).HasFilter("\"Kind\" IS NULL");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(x => x.AwaitsAnalysis);
        builder.Ignore(x => x.IsRejection);
    }
}
