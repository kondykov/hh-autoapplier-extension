using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.LlmVerdict).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => new { x.Status, x.Score });

        // Один отклик пользователя на вакансию; UserId = null (владелец по умолчанию до #9) — тоже один
        builder.HasIndex(x => new { x.VacancyId, x.UserId }).IsUnique().AreNullsDistinct(false);

        builder.HasOne(x => x.Vacancy).WithMany().HasForeignKey(x => x.VacancyId).OnDelete(DeleteBehavior.Cascade);
        // Пользователь — сущность Identity из Infrastructure: домен знает только его Id
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // Удалённый поиск не стирает отклики: остаются с правилами по умолчанию
        builder.HasOne(x => x.Search).WithMany().HasForeignKey(x => x.SearchId).OnDelete(DeleteBehavior.SetNull);
        builder.Ignore(x => x.Rules);

        // Анкета с ответами и история переходов — колонками jsonb
        builder.OwnsMany(x => x.Questions, o =>
        {
            o.ToJson();
            o.Property(q => q.Type).HasConversion<string>();
        });
        builder.OwnsMany(x => x.History, o =>
        {
            o.ToJson();
            o.Property(e => e.From).HasConversion<string>();
            o.Property(e => e.To).HasConversion<string>();
        });
    }
}