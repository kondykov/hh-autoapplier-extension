using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HhAutoApplier.Tests.Api;

// Миграция #70 на строке из прежней схемы: отдельная база в том же контейнере, общую тесты API держат на последней миграции
[Collection(ApiCollection.Name)]
public class VacancyLastSeenMigrationTests(ApiFactory factory)
{
    private const string PreviousMigration = "20261110165640_LetterRequests";

    [Fact]
    public async Task OldRows_GetLastSeenFromCollectedAt_ArchiveCheckEmpty()
    {
        var database = $"migration_{Guid.NewGuid():N}";
        var connection = await factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync($"""CREATE DATABASE "{database}" """);
            return db.Database.GetConnectionString()!;
        });
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { Database = database }.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Vacancies" ("Id", "Title", "Company", "Url", "Area", "Description", "WorkFormat", "Stack", "HasTest", "PublishedAt", "CollectedAt", "SearchName", "Imported")
            VALUES ('1', 't', 'c', 'u', 'a', 'd', 'w', ARRAY[]::text[], false, '2026-10-01T00:00:00Z', '2026-10-02T03:04:05Z', 's', false)
            """);

        await migrator.MigrateAsync();

        var vacancy = await db.Vacancies.AsNoTracking().SingleAsync();
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 3, 4, 5, TimeSpan.Zero), vacancy.LastSeenAt);
        Assert.Equal(vacancy.CollectedAt, vacancy.LastSeenAt);
        Assert.Null(vacancy.ArchiveCheckedAt);
    }
}
