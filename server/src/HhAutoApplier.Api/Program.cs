using HhAutoApplier.Core.Domain;
using HhAutoApplier.Core.Rules;
using HhAutoApplier.Infrastructure;
using HhAutoApplier.Infrastructure.Collecting;
using HhAutoApplier.Infrastructure.Hh;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.UseCors();

// Простая защита: ключ в заголовке X-Api-Key. Пустой ключ в конфиге отключает проверку (только для разработки)
var apiKey = builder.Configuration["Api:Key"];
app.Use(async (ctx, next) =>
{
    var open = ctx.Request.Path.StartsWithSegments("/health") || ctx.Request.Method == "OPTIONS";
    if (!open && !string.IsNullOrEmpty(apiKey) && ctx.Request.Headers["X-Api-Key"] != apiKey)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

// Очередь на отклик: лучшие вакансии с уже выбранным резюме
api.MapGet("/queue", async (AppDbContext db, string? search, string? resume, int? limit) =>
{
    var q = db.Vacancies.AsNoTracking().Where(v => v.Status == VacancyStatus.Queued);
    if (!string.IsNullOrWhiteSpace(search)) q = q.Where(v => v.SearchName == search);
    if (!string.IsNullOrWhiteSpace(resume)) q = q.Where(v => v.ResumeProfile == resume);
    return await q.OrderByDescending(v => v.Score).ThenByDescending(v => v.PublishedAt)
        .Take(Math.Clamp(limit ?? 20, 1, 200)).ToListAsync();
});

api.MapGet("/vacancies", async (AppDbContext db, VacancyStatus? status, string? search, int? skip, int? take) =>
{
    var q = db.Vacancies.AsNoTracking().AsQueryable();
    if (status is not null) q = q.Where(v => v.Status == status);
    if (!string.IsNullOrWhiteSpace(search)) q = q.Where(v => v.SearchName == search);
    return await q.OrderByDescending(v => v.PublishedAt).Skip(skip ?? 0).Take(Math.Clamp(take ?? 50, 1, 500)).ToListAsync();
});

api.MapPost("/vacancies/{id}/status", async (AppDbContext db, string id, StatusUpdate update) =>
{
    var v = await db.Vacancies.FindAsync(id);
    if (v is null) return Results.NotFound();
    v.Status = update.Status;
    v.StatusReason = update.Reason ?? v.StatusReason;
    if (!string.IsNullOrWhiteSpace(update.Resume)) v.ResumeProfile = update.Resume;
    if (update.Status == VacancyStatus.Applied) v.AppliedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(v);
});

api.MapGet("/stats", async (AppDbContext db) =>
{
    var rows = await db.Vacancies.GroupBy(v => v.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
    return rows.ToDictionary(r => r.Status.ToString(), r => r.Count);
});

api.MapGet("/searches", (AppDbContext db) => db.Searches.AsNoTracking().OrderBy(s => s.Name).ToListAsync());

api.MapPut("/searches", async (AppDbContext db, SearchProfile input) =>
{
    var existing = await db.Searches.FirstOrDefaultAsync(s => s.Name == input.Name);
    if (existing is null) db.Searches.Add(input);
    else
    {
        existing.Query = input.Query;
        existing.Area = input.Area;
        existing.ExtraParams = input.ExtraParams;
        existing.Enabled = input.Enabled;
        existing.Rules = input.Rules;
    }
    await db.SaveChangesAsync();
    return Results.Ok(existing ?? input);
});

api.MapDelete("/searches/{id:int}", async (AppDbContext db, int id) =>
    await db.Searches.Where(s => s.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound());

api.MapGet("/resumes", (AppDbContext db) => db.Resumes.AsNoTracking().OrderBy(r => r.Name).ToListAsync());

api.MapPut("/resumes", async (AppDbContext db, ResumeProfile input) =>
{
    var existing = await db.Resumes.FirstOrDefaultAsync(r => r.Name == input.Name);
    if (existing is null) db.Resumes.Add(input);
    else
    {
        existing.Match = input.Match;
        existing.Keywords = input.Keywords;
    }
    await db.SaveChangesAsync();
    return Results.Ok(existing ?? input);
});

// Приём вакансий от клиента (расширение/Playwright), если API hh.ru недоступен без токена
api.MapPost("/import", async (AppDbContext db, ImportRequest req) =>
{
    var search = await db.Searches.AsNoTracking().FirstOrDefaultAsync(s => s.Name == req.Search);
    if (search is null) return Results.BadRequest(new { error = $"поиск «{req.Search}» не найден" });
    var resumes = await db.Resumes.AsNoTracking().ToListAsync();

    var ids = req.Vacancies.Select(v => v.Id).Distinct().ToList();
    var known = (await db.Vacancies.Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync()).ToHashSet();
    var added = 0; var queued = 0;
    foreach (var v in req.Vacancies.DistinctBy(v => v.Id).Where(v => !known.Contains(v.Id)))
    {
        v.SearchName = search.Name;
        v.CollectedAt = DateTimeOffset.UtcNow;
        if (v.PublishedAt == default) v.PublishedAt = v.CollectedAt;
        if (v.Stack.Length == 0) v.Stack = StackExtractor.Extract($"{v.Title} {v.Description}");
        var e = VacancyEvaluator.Default.Evaluate(v, search.Rules, resumes);
        (v.Status, v.Score, v.StatusReason, v.ResumeProfile) = (e.Status, e.Score, e.Reason, e.ResumeProfile);
        db.Vacancies.Add(v);
        added++;
        if (e.Status == VacancyStatus.Queued) queued++;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { received = req.Vacancies.Count, added, queued, skipped = added - queued });
});

api.MapPost("/collect", async (VacancyCollector collector, CancellationToken ct) =>
{
    try { return Results.Ok(await collector.RunAsync(ct)); }
    catch (HhAuthException e) { return Results.Problem(e.Message, statusCode: StatusCodes.Status502BadGateway); }
});

// Отчёт по откликам: json или csv (разделитель ';', BOM — открывается в Excel)
api.MapGet("/report", async (AppDbContext db, string? format) =>
{
    var rows = await db.Vacancies.AsNoTracking().Where(v => v.Status == VacancyStatus.Applied)
        .OrderByDescending(v => v.AppliedAt).ToListAsync();
    if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)) return Results.Ok(rows);

    static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    var lines = new List<string> { "Вакансия;Стек;Компания;Оплата;Формат;Ссылка;Дата;Резюме" };
    lines.AddRange(rows.Select(v => string.Join(';',
        Q(v.Title), Q(string.Join(", ", v.Stack)), Q(v.Company), Q(FormatSalary(v)),
        Q(v.WorkFormat), Q(v.Url), Q(v.AppliedAt?.ToString("yyyy-MM-dd")), Q(v.ResumeProfile))));
    var bytes = System.Text.Encoding.UTF8.GetPreamble()
        .Concat(System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines))).ToArray();
    return Results.File(bytes, "text/csv; charset=utf-8", "applications.csv");
});

app.Run();

static string FormatSalary(Vacancy v) => (v.SalaryFrom, v.SalaryTo) switch
{
    (null, null) => "не указана",
    var (f, t) => $"{(f is null ? "" : $"от {f} ")}{(t is null ? "" : $"до {t} ")}{v.SalaryCurrency}".Trim(),
};

public record ImportRequest(string Search, List<Vacancy> Vacancies);

public record StatusUpdate(VacancyStatus Status, string? Reason, string? Resume);

public partial class Program;
