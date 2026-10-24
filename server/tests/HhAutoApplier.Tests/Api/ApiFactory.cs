using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace HhAutoApplier.Tests.Api;

// API целиком в памяти теста поверх настоящего Postgres 17 в контейнере: миграции, JSON-колонки, text[] — как в бою.
// hh.ru не используется: источник вакансий подменён, фоновый сбор не стартует
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ApiKey = "test-key";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();

    public FakeVacancySource Source { get; } = new();

    public async Task InitializeAsync() => await _db.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Api:Key", ApiKey);
        builder.UseSetting("Hh:Enabled", "false");
        builder.UseSetting("Collector:CollectOnStartup", "false");
        builder.UseSetting("Collector:IntervalMinutes", "10080");
        builder.UseSetting("Collector:DelayMinMs", "0");
        builder.UseSetting("Collector:DelayMaxMs", "0");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVacancySource>();
            services.AddSingleton<IVacancySource>(Source);
        });
    }

    public HttpClient CreateApiClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    // Чистая база перед каждым тестом: начальные резюме — как после первого старта
    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE "Vacancies", "Searches", "Resumes" RESTART IDENTITY""");
        await DbSeeder.SeedAsync(db);
        Source.Reset();
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

// Источник вакансий для тестов сбора: отдаёт заданные карточки или «блокируется», как hh.ru при капче
public sealed class FakeVacancySource : IVacancySource
{
    private readonly List<(string Id, string Title, string Description)> _cards = [];

    public bool Blocked { get; set; }

    public int DetailsLoaded { get; private set; }

    public string Name => "fake";

    public void Add(string id, string title, string description) => _cards.Add((id, title, description));

    public void Reset()
    {
        _cards.Clear();
        Blocked = false;
        DetailsLoaded = 0;
    }

    public Task<IReadOnlyList<Vacancy>> SearchAsync(SearchProfile search, int page, CancellationToken ct)
    {
        if (Blocked)
            throw new SourceBlockedException(Name, "капча");

        IReadOnlyList<Vacancy> found = page > 0
            ? []
            : _cards.Select(c => Vacancy.Create(c.Id, c.Title, $"https://hh.ru/vacancy/{c.Id}", search.Name, DateTimeOffset.UtcNow)).ToList();
        return Task.FromResult(found);
    }

    public Task<bool> LoadDetailsAsync(Vacancy vacancy, CancellationToken ct)
    {
        DetailsLoaded++;
        var card = _cards.First(c => c.Id == vacancy.Id);
        vacancy.UpdateDetails(null, card.Description, []);
        return Task.FromResult(true);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
