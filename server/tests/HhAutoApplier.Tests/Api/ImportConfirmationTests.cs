using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HhAutoApplier.Tests.Api;

// Импортная карточка (#55, #59): отклик ждёт в New, сбор подтверждает карточку по странице вакансии с лимитом за проход
[Collection(ApiCollection.Name)]
public class ImportConfirmationTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Search = "dotnet";

    private readonly HttpClient _api = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task SaveSearch(object? rules = null) =>
        await Json(await _api.PutAsJsonAsync("/api/searches", new { name = Search, query = "C#", rules = rules ?? new { } }));

    // Карточка как с выдачи расширения: обрывок описания, зарплаты нет. Источник знает эти вакансии только по странице
    private async Task<JsonElement> Import(params string[] ids)
    {
        foreach (var id in ids)
            factory.Source.AddUnlisted(id, $"C# разработчик {id}", "Полное описание: ASP.NET Core", salaryFrom: 300_000);

        return await Json(await _api.PostAsJsonAsync("/api/import", new
        {
            search = Search,
            vacancies = ids.Select(id => new { id, title = $"C# разработчик {id}", company = "Acme", description = "Обрывок описания" }),
        }));
    }

    private async Task Collect() => await Json(await _api.PostAsync("/api/collect", null));

    private async Task<JsonElement> Vacancy(string id) =>
        (await Json(await _api.GetAsync("/api/vacancies?take=200"))).EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);

    private async Task<string?> Status(string id) => (await Vacancy(id)).GetProperty("status").GetString();

    private async Task<int> CountWaiting() =>
        (await Json(await _api.GetAsync("/api/vacancies?status=New&take=200"))).GetArrayLength();

    private Task<bool> IsImported(string id) =>
        factory.WithDbAsync(db => db.Vacancies.Where(v => v.Id == id).Select(v => v.Imported).SingleAsync());

    [Fact]
    public async Task Import_WaitsInNew_ThenEvaluatedByFullCard()
    {
        // По карточке с выдачи такой поиск пропустил бы отклик: зарплаты в ней нет
        await SaveSearch(new { requireSalary = true });
        factory.Llm.Configured = true;

        var result = await Import("1");

        Assert.Equal((1, 0, 0, 1), (result.GetProperty("added").GetInt32(), result.GetProperty("queued").GetInt32(),
            result.GetProperty("skipped").GetInt32(), result.GetProperty("waiting").GetInt32()));
        var waiting = await Vacancy("1");
        Assert.Equal(("New", "Обрывок описания"), (waiting.GetProperty("status").GetString(), waiting.GetProperty("description").GetString()));
        Assert.Equal(0, factory.Source.DetailsLoaded);

        await Collect();

        var evaluated = await Vacancy("1");
        Assert.Equal(("Pending", "Полное описание: ASP.NET Core"), (evaluated.GetProperty("status").GetString(), evaluated.GetProperty("description").GetString()));
        Assert.False(await IsImported("1"));
        Assert.Equal(300_000, await factory.WithDbAsync(db => db.Vacancies.Where(v => v.Id == "1").Select(v => v.SalaryFrom).SingleAsync()));

        // Подтверждённую карточку повторный сбор не запрашивает
        await Collect();
        Assert.Equal(1, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task Import_RulesSeeFullCard_NotSnippet()
    {
        // Стоп-слово есть только в полном описании
        await SaveSearch(new { excludeKeywords = new[] { "ASP.NET" } });
        await Import("1");
        Assert.Equal("New", await Status("1"));

        await Collect();

        var skipped = await Vacancy("1");
        Assert.Equal(("Skipped", "стоп-слово «ASP.NET»"), (skipped.GetProperty("status").GetString(), skipped.GetProperty("statusReason").GetString()));
    }

    [Fact]
    public async Task Import_ConfirmedCard_EvaluatedImmediately()
    {
        await SaveSearch();
        await Import("1");
        await Collect();
        Assert.Equal("Queued", await Status("1"));

        // Карточку уже подтвердил источник: другому поиску того же пользователя ждать нечего
        var other = await Json(await _api.PutAsJsonAsync("/api/searches", new { name = "second", query = "C#", rules = new { } }));
        Assert.Equal("second", other.GetProperty("name").GetString());
        await factory.WithDbAsync(db => db.JobApplications.ExecuteDeleteAsync());

        var result = await Json(await _api.PostAsJsonAsync("/api/import", new { search = "second", vacancies = new[] { new { id = "1", title = "x", company = "y" } } }));

        Assert.Equal((1, 1, 0), (result.GetProperty("added").GetInt32(), result.GetProperty("queued").GetInt32(), result.GetProperty("waiting").GetInt32()));
    }

    [Fact]
    public async Task Confirmation_LimitedPerPass_OldestFirst()
    {
        await SaveSearch();
        var oldest = Enumerable.Range(101, 5).Select(i => i.ToString()).ToArray();
        await Import(oldest);
        await Import(Enumerable.Range(1, 20).Select(i => i.ToString()).ToArray());

        await Collect();

        Assert.Equal(20, factory.Source.DetailsLoaded);
        Assert.Equal(5, await CountWaiting());
        foreach (var id in oldest)
            Assert.Equal("Queued", await Status(id));

        // Очередь двигается: следующий проход доделывает остальное
        await Collect();

        Assert.Equal(25, factory.Source.DetailsLoaded);
        Assert.Equal(0, await CountWaiting());
    }

    [Fact]
    public async Task Confirmation_LimitSharedWithCardsFoundInSearch()
    {
        await SaveSearch();
        var ids = Enumerable.Range(1, 25).Select(i => i.ToString()).ToArray();
        await Import(ids);
        foreach (var id in ids)
            factory.Source.Add(id, $"C# разработчик {id}", "Описание из выдачи");

        await Collect();

        Assert.Equal(20, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task UnavailablePage_Skipped_NotCheckedAgain()
    {
        await SaveSearch();
        await Import("1");
        factory.Source.NoDetails.Add("1");

        await Collect();

        var skipped = await Vacancy("1");
        Assert.Equal(("Skipped", "вакансия недоступна"), (skipped.GetProperty("status").GetString(), skipped.GetProperty("statusReason").GetString()));
        Assert.Equal(1, factory.Source.DetailsLoaded);
        Assert.True(await IsImported("1"));

        await Collect();

        Assert.Equal(1, factory.Source.DetailsLoaded);
        Assert.Equal("Skipped", await Status("1"));
    }

    [Fact]
    public async Task SourceBlockedOnPage_ApplicationsKeepWaiting_DoneBeforeIsSaved()
    {
        await SaveSearch();
        await Import("1");
        await Import("2");
        factory.Source.BlockedDetails.Add("2");

        Assert.Equal(HttpStatusCode.BadGateway, (await _api.PostAsync("/api/collect", null)).StatusCode);

        Assert.Equal("Queued", await Status("1"));
        Assert.Equal("New", await Status("2"));
        Assert.True(await IsImported("2"));

        factory.Source.BlockedDetails.Clear();
        await Collect();

        Assert.Equal("Queued", await Status("2"));
    }

    [Fact]
    public async Task NoSources_ApplicationsKeepWaiting()
    {
        await SaveSearch();
        await Import("1");

        // Hh:Enabled = false: источника нет, спросить о вакансии некого
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.RemoveAll<IVacancySource>()));
        var admin = app.CreateDefaultClient(new Uri("http://localhost"), new LoginHandler(ApiFactory.AdminEmail, ApiFactory.AdminPassword), new CookieContainerHandler());
        await Json(await admin.PostAsync("/api/collect", null));

        Assert.Equal("New", await Status("1"));
    }

    [Fact]
    public async Task AlreadyDecidedApplications_NotTouched()
    {
        await SaveSearch();
        await Import("1", "2");
        // Отклик из New становится отправленным только восстановлением по чату hh.ru: статусом через API — нельзя (#60)
        await factory.WithDbAsync(async db =>
        {
            var application = await db.JobApplications.SingleAsync(a => a.VacancyId == "1");
            application.RecoverApplied(DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });
        await Json(await _api.PostAsJsonAsync("/api/vacancies/2/status", new { status = "Skipped", reason = "не интересно" }));
        factory.Source.NoDetails.UnionWith(["1", "2"]);

        await Collect();

        Assert.Equal(0, factory.Source.DetailsLoaded);
        Assert.Equal("Applied", await Status("1"));
        var skipped = await Vacancy("2");
        Assert.Equal(("Skipped", "не интересно"), (skipped.GetProperty("status").GetString(), skipped.GetProperty("statusReason").GetString()));
    }

    [Fact]
    public async Task SaveConflict_DoesNotFailPass_UserDecisionKept()
    {
        await SaveSearch();
        await Import("1");
        await Import("2");

        // Пока грузится страница, пользователь сам пропустил отклик: xmin отклика изменился
        factory.Source.WhileLoadingDetails = async id =>
        {
            factory.Source.WhileLoadingDetails = null;
            await Json(await _api.PostAsJsonAsync($"/api/vacancies/{id}/status", new { status = "Skipped", reason = "сам" }));
        };

        await Collect();

        var mine = await Vacancy("1");
        Assert.Equal(("Skipped", "сам"), (mine.GetProperty("status").GetString(), mine.GetProperty("statusReason").GetString()));
        Assert.Equal("New", await Status("2"));

        await Collect();

        Assert.Equal("Queued", await Status("2"));
        Assert.Equal("Skipped", await Status("1"));
    }
}
