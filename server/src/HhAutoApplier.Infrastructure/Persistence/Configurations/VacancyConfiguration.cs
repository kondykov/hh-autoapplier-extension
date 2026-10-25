using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class VacancyConfiguration : IEntityTypeConfiguration<Vacancy>
{
    public void Configure(EntityTypeBuilder<Vacancy> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.LlmVerdict).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => new { x.Status, x.Score });
        builder.HasIndex(x => x.SearchName);

        // Value object собирается из колонок SalaryFrom/SalaryTo/SalaryCurrency
        builder.Ignore(x => x.Salary);

        // Анкета с ответами — одной колонкой jsonb
        builder.OwnsMany(x => x.Questions, o =>
        {
            o.ToJson();
            o.Property(q => q.Type).HasConversion<string>();
        });
    }
}
