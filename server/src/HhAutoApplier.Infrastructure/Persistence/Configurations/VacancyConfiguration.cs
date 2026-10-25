using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HhAutoApplier.Infrastructure.Persistence.Configurations;

public class VacancyConfiguration : IEntityTypeConfiguration<Vacancy>
{
    public void Configure(EntityTypeBuilder<Vacancy> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.SearchName);

        // Value object собирается из колонок SalaryFrom/SalaryTo/SalaryCurrency
        builder.Ignore(x => x.Salary);
    }
}