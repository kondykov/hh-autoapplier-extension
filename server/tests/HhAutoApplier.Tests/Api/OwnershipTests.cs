using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Commands.Llm;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Tests.Api;

// Владельцы данных (#9): поиски, резюме и отклики у каждого пользователя свои, вакансия — общая
[Collection(ApiCollection.Name)]
public class OwnershipTests(ApiFactory factory) : IAsyncLifetime
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

    // Пользователь создан администратором; клиент войдёт по паролю при первом запросе
    private async Task<(string Id, HttpClient Client)> CreateUser(string email)
    {
        var user = await Json(await _admin.PostAsJsonAsync("/api/users", new { email, password = Password, role = "User" }));
        return (user.GetProperty("id").GetString()!, factory.CreateLoggedInClient(email, Password));
    }

    private static async Task SaveSearch(HttpClient client, string name, string query = "C#", params string[] exclude) =>
        await Json(await client.PutAsJsonAsync("/api/searches", new { name, query, rules = new { excludeKeywords = exclude } }));

    private static async Task Import(HttpClient client, string search, params string[] ids) =>
        await Json(await client.PostAsJsonAsync("/api/import", new
        {
            search,
            vacancies = ids.Select(id => new { id, title = "C# разработчик", company = "Acme", description = "ASP.NET Core" }),
        }));

    private static async Task<List<string?>> Names(HttpClient client, string url) =>
        (await Json(await client.GetAsync(url))).EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();

    private static async Task<List<JsonElement>> Vacancies(HttpClient client, string url = "/api/vacancies") =>
        (await Json(await client.GetAsync(url))).EnumerateArray().ToList();

    [Fact]
    public async Task TwoUsers_SameSearchName_DoNotSeeEachOther()
    {
        var (_, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");

        // Имя поиска уникально только в пределах пользователя
        await SaveSearch(a, "dotnet", "C# A");
        await SaveSearch(b, "dotnet", "C# B");
        var searchA = Assert.Single((await Json(await a.GetAsync("/api/searches"))).EnumerateArray());
        var searchB = Assert.Single((await Json(await b.GetAsync("/api/searches"))).EnumerateArray());
        Assert.Equal(("C# A", "C# B"), (searchA.GetProperty("query").GetString(), searchB.GetProperty("query").GetString()));
        Assert.NotEqual(searchA.GetProperty("id").GetInt32(), searchB.GetProperty("id").GetInt32());
        Assert.Empty((await Json(await _admin.GetAsync("/api/searches"))).EnumerateArray());

        // Резюме: начальные — у администратора, своё резюме A не видно B
        await Json(await a.PutAsJsonAsync("/api/resumes", new { name = "Резюме A", match = "C#", keywords = new[] { "c#" } }));
        Assert.Equal(["Резюме A"], await Names(a, "/api/resumes"));
        Assert.Empty(await Names(b, "/api/resumes"));
        Assert.Equal([".NET", "PHP"], await Names(_admin, "/api/resumes"));

        // Отклики и статистика — только свои
        await Import(a, "dotnet", "1");
        Assert.Equal(["1"], (await Vacancies(a)).Select(v => v.GetProperty("id").GetString()));
        Assert.Empty(await Vacancies(b));
        Assert.Empty(await Vacancies(b, "/api/queue"));
        Assert.Equal(1, (await Json(await a.GetAsync("/api/stats"))).GetProperty("Queued").GetInt32());
        Assert.All((await Json(await b.GetAsync("/api/stats"))).EnumerateObject(), p => Assert.Equal(0, p.Value.GetInt32()));
    }

    [Fact]
    public async Task OtherUsersVacancy_Is404_ImportGivesOwnApplication()
    {
        var (_, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        await SaveSearch(a, "a-search");
        await SaveSearch(b, "b-search");
        await Import(a, "a-search", "1");

        // Вакансия есть, но отклика у B нет — для него её нет
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/vacancies/1/status", new { status = "Applied" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/vacancies/1/review", new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/vacancies/1/questionnaire",
            new { questions = new[] { new { question = "Опыт?", type = "Text" } } })).StatusCode);

        // Импорт известной вакансии в свой поиск даёт B свой отклик; статус A не меняется
        await Import(b, "b-search", "1");
        var applied = await Json(await b.PostAsJsonAsync("/api/vacancies/1/status", new { status = "Applied" }));
        Assert.Equal(("Applied", "b-search"), (applied.GetProperty("status").GetString(), applied.GetProperty("searchName").GetString()));
        var vacancyA = Assert.Single(await Vacancies(a));
        Assert.Equal(("Queued", "a-search"), (vacancyA.GetProperty("status").GetString(), vacancyA.GetProperty("searchName").GetString()));
        Assert.Equal(1, await factory.WithDbAsync(db => db.Vacancies.CountAsync()));

        // Импорт в чужой поиск не проходит: поиск ищется среди своих
        Assert.Equal(HttpStatusCode.BadRequest, (await b.PostAsJsonAsync("/api/import", new { search = "a-search", vacancies = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task Collect_SharedVacancy_ApplicationPerSearchOwner_WithOwnRules()
    {
        var (_, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        await SaveSearch(a, "a-search");
        await SaveSearch(b, "b-search", "C#", "kafka");
        factory.Source.Add("10", "C# разработчик", "ASP.NET Core, Kafka");

        var collected = await Json(await _admin.PostAsync("/api/collect", null));
        Assert.Equal((2, 1, 1), (collected.GetProperty("added").GetInt32(), collected.GetProperty("queued").GetInt32(),
            collected.GetProperty("skipped").GetInt32()));

        // Вакансия одна и загружена один раз, отклики — у каждого владельца поиска со своими правилами
        Assert.Equal(1, factory.Source.DetailsLoaded);
        Assert.Equal((1, 2), await factory.WithDbAsync(async db => (await db.Vacancies.CountAsync(), await db.JobApplications.CountAsync())));

        var vacancyA = Assert.Single(await Vacancies(a));
        Assert.Equal(("10", "Queued", "a-search"), (vacancyA.GetProperty("id").GetString(), vacancyA.GetProperty("status").GetString(),
            vacancyA.GetProperty("searchName").GetString()));
        var vacancyB = Assert.Single(await Vacancies(b));
        Assert.Equal(("10", "Skipped", "b-search", "стоп-слово «kafka»"), (vacancyB.GetProperty("id").GetString(),
            vacancyB.GetProperty("status").GetString(), vacancyB.GetProperty("searchName").GetString(), vacancyB.GetProperty("statusReason").GetString()));
        Assert.Empty(await Vacancies(_admin));

        // Повторный сбор ничего не добавляет и не загружает
        Assert.Equal(0, (await Json(await _admin.PostAsync("/api/collect", null))).GetProperty("added").GetInt32());
        Assert.Equal(1, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task LlmWorkers_DistributeBatchesAcrossUsers()
    {
        var (idA, a) = await CreateUser("fair-a@test.local");
        var (idB, b) = await CreateUser("fair-b@test.local");
        var userA = Guid.Parse(idA);
        var userB = Guid.Parse(idB);
        await SaveSearch(a, "a-search");
        await SaveSearch(b, "b-search");

        factory.Llm.Configured = true;
        await Import(a, "a-search", "fair-a-1", "fair-a-2");
        await Import(b, "b-search", "fair-b-1", "fair-b-2");

        const string evaluation = """{"verdict":"fit","score":80,"resume":null,"reason":"подходит"}""";
        factory.Llm.Answers.Enqueue(evaluation);
        factory.Llm.Answers.Enqueue(evaluation);
        await Json(await _admin.PostAsJsonAsync("/api/llm/evaluate", new { limit = 2 }));

        var afterFirstBatch = await factory.WithDbAsync(db => db.JobApplications
            .Where(application => application.UserId == userA || application.UserId == userB)
            .Select(application => new { application.UserId, application.Status })
            .ToListAsync());
        Assert.Single(afterFirstBatch, application => application.UserId == userA && application.Status == VacancyStatus.Queued);
        Assert.Single(afterFirstBatch, application => application.UserId == userA && application.Status == VacancyStatus.Pending);
        Assert.Single(afterFirstBatch, application => application.UserId == userB && application.Status == VacancyStatus.Queued);
        Assert.Single(afterFirstBatch, application => application.UserId == userB && application.Status == VacancyStatus.Pending);

        factory.Llm.Answers.Enqueue(evaluation);
        factory.Llm.Answers.Enqueue(evaluation);
        await Json(await _admin.PostAsJsonAsync("/api/llm/evaluate", new { limit = 2 }));

        const string letter = "Здравствуйте! Я заинтересован в вакансии и буду рад обсудить задачи на собеседовании.";
        factory.Llm.Answers.Enqueue(letter);
        factory.Llm.Answers.Enqueue(letter);
        await Json(await _admin.PostAsJsonAsync("/api/llm/letters", new { limit = 2 }));

        var questionnaireVacancies = await factory.WithDbAsync(db => db.JobApplications
            .Where(application => (application.UserId == userA || application.UserId == userB)
                                  && application.Status == VacancyStatus.Queued
                                  && application.CoverLetter == null)
            .Select(application => new { application.UserId, application.VacancyId })
            .ToListAsync());
        var vacancyA = Assert.Single(questionnaireVacancies, application => application.UserId == userA).VacancyId;
        var vacancyB = Assert.Single(questionnaireVacancies, application => application.UserId == userB).VacancyId;
        object questionnaire = new { questions = new[] { new { question = "Опыт?", type = "Text" } } };
        await Json(await a.PostAsJsonAsync($"/api/vacancies/{vacancyA}/questionnaire", questionnaire));
        await Json(await b.PostAsJsonAsync($"/api/vacancies/{vacancyB}/questionnaire", questionnaire));

        factory.Llm.Answers.Enqueue("""{"answers":["Расскажу на собеседовании"]}""");
        factory.Llm.Answers.Enqueue("""{"answers":["Расскажу на собеседовании"]}""");
        await Json(await _admin.PostAsJsonAsync("/api/llm/answers", new { limit = 2 }));

        var completedQuestionnaires = await factory.WithDbAsync(db => db.JobApplications
            .Where(application => application.VacancyId == vacancyA || application.VacancyId == vacancyB)
            .ToListAsync());
        Assert.Equal(2, completedQuestionnaires.Count);
        Assert.All(completedQuestionnaires, application => Assert.Equal(VacancyStatus.Queued, application.Status));
        Assert.All(completedQuestionnaires, application => Assert.Equal(["Расскажу на собеседовании"], application.Questions.Single().Answer));

        var scheduler = factory.Services.GetRequiredService<LlmWorkScheduler>();
        using var inFlight = scheduler.ReserveFair(completedQuestionnaires, 1);
        using var nextBatch = scheduler.ReserveFair(completedQuestionnaires, 1);
        var first = Assert.Single(inFlight.Applications);
        var second = Assert.Single(nextBatch.Applications);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.UserId, second.UserId);

        Assert.Equal(8, await factory.WithDbAsync(db => db.LlmCalls.CountAsync()));
    }

    [Fact]
    public async Task UserIdParam_AdminReadsOthers_UserForbidden()
    {
        var (idA, a) = await CreateUser("a@test.local");
        var (_, b) = await CreateUser("b@test.local");
        await SaveSearch(a, "a-search");
        await Import(a, "a-search", "1");
        await Json(await a.PutAsJsonAsync("/api/resumes", new { name = "Резюме A", match = "C#" }));

        Assert.Equal(["1"], (await Vacancies(_admin, $"/api/vacancies?userId={idA}")).Select(v => v.GetProperty("id").GetString()));
        Assert.Equal(["a-search"], await Names(_admin, $"/api/searches?userId={idA}"));
        Assert.Equal(["Резюме A"], await Names(_admin, $"/api/resumes?userId={idA}"));
        Assert.Equal(1, (await Json(await _admin.GetAsync($"/api/stats?userId={idA}"))).GetProperty("Queued").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/api/report?userId={idA}")).StatusCode);

        // Свой id — можно, чужой — только администратору
        Assert.Equal(["1"], (await Vacancies(a, $"/api/vacancies?userId={idA}")).Select(v => v.GetProperty("id").GetString()));
        foreach (var url in new[] { "/api/vacancies", "/api/searches", "/api/resumes", "/api/stats", "/api/report" })
            Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"{url}?userId={idA}")).StatusCode);
    }

    [Fact]
    public async Task ApiKey_AdminEmailUsersData_WithUserRole()
    {
        await SaveSearch(_admin, "admin-search");
        var key = factory.CreateKeyClient();

        Assert.Equal(["admin-search"], await Names(key, "/api/searches"));
        Assert.Equal([".NET", "PHP"], await Names(key, "/api/resumes"));
        await Import(key, "admin-search", "1");
        Assert.Equal(["1"], (await Vacancies(_admin)).Select(v => v.GetProperty("id").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await key.PostAsync("/api/collect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await key.PostAsync("/api/llm/check", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await key.GetAsync("/api/users")).StatusCode);
        // Ключ — роль User: чужие данные по ?userId недоступны
        var (idA, _) = await CreateUser("a@test.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await key.GetAsync($"/api/vacancies?userId={idA}")).StatusCode);
    }

    [Fact]
    public async Task Startup_AdoptsOrphans_ExceptClashingWithAdmin()
    {
        await SaveSearch(_admin, "dotnet");
        await factory.WithDbAsync(async db =>
        {
            db.Searches.Add(SearchProfile.Create("orphan", "C#", null, null, null, true, userId: null));
            db.Searches.Add(SearchProfile.Create("dotnet", "PHP", null, null, null, true, userId: null));
            return await db.SaveChangesAsync();
        });

        await using (var scope = factory.Services.CreateAsyncScope())
            await IdentitySeeder.SeedAsync(scope.ServiceProvider);

        // Сирота перешёл администратору из Admin:Email; совпавший по имени с его поиском остался у владельца по умолчанию
        Assert.Equal(["dotnet", "orphan"], (await Names(_admin, "/api/searches")).Order());
        var left = await factory.WithDbAsync(db => db.Searches.Where(s => s.UserId == null).Select(s => s.Query).ToListAsync());
        Assert.Equal(["PHP"], left);
    }

    [Fact]
    public async Task Startup_AdoptedApplication_PointsToAdminsSameNameSearch()
    {
        await SaveSearch(_admin, "dotnet");
        await factory.WithDbAsync(async db =>
        {
            var orphan = SearchProfile.Create("dotnet", "PHP", null, null, null, true, userId: null);
            db.Searches.Add(orphan);
            await db.SaveChangesAsync();
            var vacancy = Vacancy.Create("50", "C# разработчик", "https://hh.ru/vacancy/50", "dotnet", DateTimeOffset.UtcNow);
            db.Vacancies.Add(vacancy);
            db.JobApplications.Add(JobApplication.Create(vacancy, null, DateTimeOffset.UtcNow, orphan.Id));
            return await db.SaveChangesAsync();
        });

        await using (var scope = factory.Services.CreateAsyncScope())
            await IdentitySeeder.SeedAsync(scope.ServiceProvider);

        // Отклик перешёл администратору, а его поиск остался ничейным (совпало имя): ссылка — на поиск администратора
        var row = await factory.WithDbAsync(db => db.JobApplications.Where(a => a.VacancyId == "50")
            .Select(a => new { a.UserId, SearchOwner = a.Search!.UserId }).SingleAsync());
        Assert.NotNull(row.UserId);
        Assert.Equal(row.UserId, row.SearchOwner);
        Assert.Equal("dotnet", Assert.Single(await Vacancies(_admin)).GetProperty("searchName").GetString());
    }
}
