using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Tests.Api;

// Границы между пользователями после ревью безопасности: импорт не подменяет общую карточку, ключ не переходит
// к другому администратору, чужие поиски и резюме не участвуют в чужих откликах и промптах
[Collection(ApiCollection.Name)]
public class SecurityTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "user-password-1";

    private readonly HttpClient _admin = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(string Id, HttpClient Client)> CreateUser(string email, string role = "User")
    {
        var user = await Json(await _admin.PostAsJsonAsync("/api/users", new { email, password = Password, role }));
        return (user.GetProperty("id").GetString()!, factory.CreateLoggedInClient(email, Password));
    }

    private static async Task<int> SaveSearch(HttpClient client, string name) =>
        (await Json(await client.PutAsJsonAsync("/api/searches", new { name, query = "C#" }))).GetProperty("id").GetInt32();

    private static async Task Import(HttpClient client, string search, string id, string? url = null, string description = "ASP.NET Core") =>
        await Json(await client.PostAsJsonAsync("/api/import", new
        {
            search,
            vacancies = new[] { new { id, title = "C# разработчик", company = "Acme", url, description } },
        }));

    private static async Task<List<JsonElement>> Vacancies(HttpClient client) =>
        (await Json(await client.GetAsync("/api/vacancies"))).EnumerateArray().ToList();

    [Fact]
    public async Task Import_UnsafeUrlAndFakeCard_ReplacedBySourceOnCollect()
    {
        var (_, a) = await CreateUser("a@test.local");
        await SaveSearch(a, "a-search");
        await Import(a, "a-search", "7", url: "javascript:alert(1)", description: "Пишите на evil@example.com");

        // Ссылка клиента не прошла, карточка помечена как импортированная
        var imported = Assert.Single(await Vacancies(a));
        Assert.Equal("https://hh.ru/vacancy/7", imported.GetProperty("url").GetString());
        Assert.True(await factory.WithDbAsync(db => db.Vacancies.Where(v => v.Id == "7").Select(v => v.Imported).SingleAsync()));

        // Сбор администратора встретил вакансию: карточку и подробности даёт источник, а не клиент
        await SaveSearch(_admin, "admin-search");
        factory.Source.Add("7", "C# разработчик (источник)", "Описание из источника");
        Assert.Equal(1, (await Json(await _admin.PostAsync("/api/collect", null))).GetProperty("added").GetInt32());
        Assert.Equal(1, factory.Source.DetailsLoaded);

        var vacancy = await factory.WithDbAsync(db => db.Vacancies.AsNoTracking().SingleAsync(v => v.Id == "7"));
        Assert.Equal(("Описание из источника", "C# разработчик (источник)", "https://hh.ru/vacancy/7", false),
            (vacancy.Description, vacancy.Title, vacancy.Url, vacancy.Imported));

        var adminVacancy = Assert.Single(await Vacancies(_admin));
        Assert.Equal(("Описание из источника", "admin-search"),
            (adminVacancy.GetProperty("description").GetString(), adminVacancy.GetProperty("searchName").GetString()));
        Assert.Equal("Описание из источника", Assert.Single(await Vacancies(a)).GetProperty("description").GetString());

        // Подтверждённую карточку повторный сбор заново не загружает
        await Json(await _admin.PostAsync("/api/collect", null));
        Assert.Equal(1, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task Collect_ImportedCardDetailsFail_StaysImported_OthersWait()
    {
        var (_, a) = await CreateUser("a@test.local");
        await SaveSearch(a, "a-search");
        await Import(a, "a-search", "7", description: "Текст клиента");

        // Источник нашёл вакансию в выдаче, но её страница не открылась: карточка не подтверждена
        await SaveSearch(_admin, "admin-search");
        factory.Source.Add("7", "C# разработчик (источник)", "Описание из источника");
        factory.Source.NoDetails.Add("7");
        await Json(await _admin.PostAsync("/api/collect", null));

        var vacancy = await factory.WithDbAsync(db => db.Vacancies.AsNoTracking().SingleAsync(v => v.Id == "7"));
        Assert.Equal(("Текст клиента", true), (vacancy.Description, vacancy.Imported));
        // Отклик администратора на неподтверждённую чужую карточку не оценён — ждёт
        Assert.Equal("New", Assert.Single(await Vacancies(_admin)).GetProperty("status").GetString());

        // Страница открылась — карточка целиком из источника, ждавший отклик оценён
        factory.Source.NoDetails.Clear();
        await Json(await _admin.PostAsync("/api/collect", null));
        vacancy = await factory.WithDbAsync(db => db.Vacancies.AsNoTracking().SingleAsync(v => v.Id == "7"));
        Assert.Equal(("Описание из источника", "C# разработчик (источник)", false), (vacancy.Description, vacancy.Title, vacancy.Imported));
        Assert.Equal("Queued", Assert.Single(await Vacancies(_admin)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Import_OtherUsersUnconfirmedCard_WaitsUntilSourceConfirms()
    {
        var (_, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        await SaveSearch(a, "a-search");
        await SaveSearch(b, "b-search");
        await Import(b, "b-search", "1", description: "Текст B");

        // Карточку создал импорт B: отклик A по ней не оценивается
        var result = await Json(await a.PostAsJsonAsync("/api/import", new
        {
            search = "a-search",
            vacancies = new[] { new { id = "1", title = "C# разработчик", company = "Acme", description = "Текст A" } },
        }));
        Assert.Equal((1, 0, 0, 1), (result.GetProperty("added").GetInt32(), result.GetProperty("queued").GetInt32(),
            result.GetProperty("skipped").GetInt32(), result.GetProperty("waiting").GetInt32()));
        Assert.Equal("New", Assert.Single(await Vacancies(a)).GetProperty("status").GetString());

        // Вакансия не попадает в выдачу поисков: сбор подтверждает карточку по странице вакансии и оценивает отклик A
        factory.Source.AddUnlisted("1", "C# разработчик (источник)", "Описание из источника");
        await Json(await _admin.PostAsync("/api/collect", null));

        var mine = Assert.Single(await Vacancies(a));
        Assert.Equal(("Queued", "Описание из источника"), (mine.GetProperty("status").GetString(), mine.GetProperty("description").GetString()));
        Assert.Equal("Описание из источника", Assert.Single(await Vacancies(b)).GetProperty("description").GetString());
    }

    [Fact]
    public async Task SharedKeyWithTokenPrefix_NotAToken_401()
    {
        // Старый общий ключ, похожий на токен, — не токен из базы (#42)
        await using var app = factory.WithWebHostBuilder(b => b.UseSetting("Api:Key", "hh_shared-key"));
        var key = app.CreateClient();
        key.DefaultRequestHeaders.Add("X-Api-Key", "hh_shared-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await key.GetAsync("/api/stats")).StatusCode);
    }

    [Fact]
    public async Task Token_OwnerBlocked_401_EvenWithAnotherActiveAdmin()
    {
        var users = await Json(await _admin.GetAsync("/api/users"));
        var adminId = users.EnumerateArray().Single().GetProperty("id").GetString();
        var (_, second) = await CreateUser("second@test.local", "Admin");
        var key = factory.CreateTokenClient();
        Assert.Equal(HttpStatusCode.OK, (await key.GetAsync("/api/stats")).StatusCode);

        // Токен — администратора; после его блокировки токен не действует и не переходит ко второму администратору
        await Json(await second.PutAsJsonAsync($"/api/users/{adminId}/blocked", new { blocked = true }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await key.GetAsync("/api/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await key.GetAsync("/api/searches")).StatusCode);

        await Json(await second.PutAsJsonAsync($"/api/users/{adminId}/blocked", new { blocked = false }));
        Assert.Equal(HttpStatusCode.OK, (await key.GetAsync("/api/stats")).StatusCode);
    }

    [Fact]
    public async Task DeleteOtherUsersSearch_404_SearchKept()
    {
        var (_, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        var id = await SaveSearch(a, "a-search");

        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/searches/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/searches/{id}")).StatusCode);
        Assert.True(await factory.WithDbAsync(db => db.Searches.AnyAsync(s => s.Id == id)));
        Assert.Single((await Json(await a.GetAsync("/api/searches"))).EnumerateArray());
    }

    [Fact]
    public async Task Collect_SkipsBlockedUsersSearches()
    {
        var (idA, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        await SaveSearch(a, "a-search");
        await SaveSearch(b, "b-search");
        await Json(await _admin.PutAsJsonAsync($"/api/users/{idA}/blocked", new { blocked = true }));
        factory.Source.Add("10", "C# разработчик", "ASP.NET Core");

        Assert.Equal(1, (await Json(await _admin.PostAsync("/api/collect", null))).GetProperty("added").GetInt32());

        // Отклик только у B; у заблокированного A — ни одного
        var owners = await factory.WithDbAsync(db => db.JobApplications.Select(x => x.UserId).ToListAsync());
        Assert.DoesNotContain(Guid.Parse(idA), owners.Select(o => o!.Value));
        Assert.Single(await Vacancies(b));
    }

    [Fact]
    public async Task DeletedSearch_SearchNameEmpty_NotOtherUsersName()
    {
        var (_, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        await SaveSearch(b, "b-search");
        var idA = await SaveSearch(a, "a-search");
        // Карточку создал импорт B: в ней имя его поиска
        await Import(b, "b-search", "1");
        await Import(a, "a-search", "1");
        Assert.Equal("a-search", Assert.Single(await Vacancies(a)).GetProperty("searchName").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/api/searches/{idA}")).StatusCode);

        Assert.Equal("", Assert.Single(await Vacancies(a)).GetProperty("searchName").GetString());
        Assert.Equal("b-search", Assert.Single(await Vacancies(b)).GetProperty("searchName").GetString());
    }

    [Fact]
    public async Task LlmLetters_SameResumeName_PromptUsesOwnersResume()
    {
        var (_, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        await Json(await a.PutAsJsonAsync("/api/resumes", new
        {
            name = ".NET", match = "C#", keywords = new[] { "c#" }, summary = "Опыт A: пять лет C#", letterTemplate = "С уважением, Анна",
        }));
        await Json(await b.PutAsJsonAsync("/api/resumes", new
        {
            name = ".NET", match = "C#", keywords = new[] { "c#" }, summary = "Опыт B: три года ASP.NET", letterTemplate = "С уважением, Борис",
        }));
        await SaveSearch(a, "a-search");
        await SaveSearch(b, "b-search");
        // Без LLM на импорте отклики сразу в очереди
        await Import(a, "a-search", "1");
        await Import(b, "b-search", "2");

        factory.Llm.Configured = true;
        factory.Llm.Answers.Enqueue("Здравствуйте! Пишу на C# несколько лет и хочу к вам в команду.\n\nС уважением");
        factory.Llm.Answers.Enqueue("Здравствуйте! Пишу на C# несколько лет и хочу к вам в команду.\n\nС уважением");
        Assert.Equal(2, (await Json(await _admin.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }))).GetProperty("written").GetInt32());

        var prompts = await factory.WithDbAsync(db => db.LlmCalls.Where(c => c.Purpose == "cover-letter")
            .ToDictionaryAsync(c => c.VacancyId!, c => c.Prompt));
        Assert.Contains("Опыт B: три года ASP.NET", prompts["2"]);
        Assert.Contains("С уважением, Борис", prompts["2"]);
        Assert.DoesNotContain("Опыт A", prompts["2"]);
        Assert.DoesNotContain("Анна", prompts["2"]);
        Assert.Contains("Опыт A: пять лет C#", prompts["1"]);
        Assert.DoesNotContain("Борис", prompts["1"]);
    }
}
