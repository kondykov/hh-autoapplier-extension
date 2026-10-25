using System.Net.Http.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;
using HhAutoApplier.Infrastructure.Identity;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
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

    public const string AdminEmail = "admin@test.local";

    public const string AdminPassword = "admin-password-1";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeVacancySource Source { get; } = new();

    public FakeLlmClient Llm { get; } = new();

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
        builder.UseSetting("Admin:Email", AdminEmail);
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Hh:Enabled", "false");
        builder.UseSetting("Collector:CollectOnStartup", "false");
        builder.UseSetting("Collector:IntervalMinutes", "10080");
        builder.UseSetting("Collector:DelayMinMs", "0");
        builder.UseSetting("Collector:DelayMaxMs", "0");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVacancySource>();
            services.AddSingleton<IVacancySource>(Source);
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient>(Llm);
            // Блокировка и смена пароля обрывают сессию сразу, а не через минуту
            services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        });
    }

    // Клиент администратора: вход по паролю (cookie) при первом запросе — уже после ResetAsync, когда учётка пересоздана
    public HttpClient CreateApiClient() => CreateLoggedInClient(AdminEmail, AdminPassword);

    // Клиент с входом по паролю при первом запросе; cookie сессии сохраняются между запросами
    public HttpClient CreateLoggedInClient(string email, string password) =>
        CreateDefaultClient(new Uri("http://localhost"), new LoginHandler(email, password), new CookieContainerHandler());

    // Клиент по общему ключу API (X-Api-Key): действует от имени администратора из Admin:Email, но с ролью User
    public HttpClient CreateKeyClient()
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
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE "Vacancies", "Searches", "Resumes", "LlmCalls", "AspNetUsers", "AspNetRoles" RESTART IDENTITY CASCADE""");
        await DbSeeder.SeedAsync(db);
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
        Source.Reset();
        Llm.Reset();
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

// Перед первым запросом клиента входит по паролю; ответ входа проверяется, чтобы тест падал с понятной причиной
public sealed class LoginHandler(string email, string password) : DelegatingHandler
{
    private bool _loggedIn;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!_loggedIn)
        {
            var login = new HttpRequestMessage(HttpMethod.Post, new Uri(request.RequestUri!, "/api/auth/login"))
            {
                Content = JsonContent.Create(new { email, password }),
            };
            var response = await base.SendAsync(login, ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Вход {email} не удался: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");

            _loggedIn = true;
        }

        return await base.SendAsync(request, ct);
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

// LLM для тестов: по умолчанию не настроена, как без LLM_API_KEY; ответы — по очереди из Answers
public sealed class FakeLlmClient : ILlmClient
{
    public Queue<string> Answers { get; } = new();

    public bool Configured { get; set; }

    public int Calls { get; private set; }

    public bool IsConfigured => Configured;

    public void Reset()
    {
        Answers.Clear();
        Configured = false;
        Calls = 0;
    }

    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (!Configured)
            throw new LlmUnavailableException("LLM не настроена: задайте LLM_API_KEY");

        Calls++;
        if (!Answers.TryDequeue(out var answer))
            throw new LlmUnavailableException("дневной лимит запросов исчерпан");

        return Task.FromResult(new LlmResult(answer, "fake-model", 100, 20));
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
