using HhAutoApplier.Domain.Telegram;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class TelegramChatConfiguration : IEntityTypeConfiguration<TelegramChat>
{
    public void Configure(EntityTypeBuilder<TelegramChat> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Username).HasMaxLength(64);
        builder.Property(x => x.CodeHash).HasMaxLength(64);
        // У пользователя один чат, чат — у одного пользователя
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.ChatId).IsUnique();
        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(x => x.Linked);
    }
}
