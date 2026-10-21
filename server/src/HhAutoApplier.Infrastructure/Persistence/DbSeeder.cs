using HhAutoApplier.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Infrastructure.Persistence;

public static class DbSeeder
{
    // Резюме по умолчанию создаются только в пустой таблице и дальше редактируются через API
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Resumes.AnyAsync(ct)) return;
        db.Resumes.AddRange(
            new ResumeProfile
            {
                Name = ".NET", Match = "C# .NET",
                Keywords = ["c#", ".net", "asp.net", "dotnet", "entity framework", "ef core", "blazor"],
            },
            new ResumeProfile
            {
                Name = "PHP", Match = "PHP",
                Keywords = ["php", "laravel", "symfony", "yii", "bitrix", "битрикс", "nuxt"],
            });
        await db.SaveChangesAsync(ct);
    }
}
