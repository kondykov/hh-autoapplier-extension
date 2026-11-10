using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Applications;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HhAutoApplier.Tests.Api;

// Вакансия ушла в архив, пока отклик ждал (#70): проход сбора проверяет страницы незавершённых откликов с лимитом
[Collection(ApiCollection.Name)]
public class ArchiveCheckTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Search = "dotnet";

    private const string Archived = "вакансия в архиве";

    private const string Unavailable = "вакансия недоступна";

    private readonly HttpClient _api = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task SaveSearch() =>
        await Json(await _api.PutAsJsonAsync("/api/searches", new { name = Search, query = "C#", rules = new { } }));

    private async Task Import(params string[] ids)
    {
        foreach (var id in ids)
            factory.Source.AddUnlisted(id, $"C# разработчик {id}", "ASP.NET Core");

        await Json(await _api.PostAsJsonAsync("/api/import", new
        {
            search = Search,
            vacancies = ids.Select(id => new { id, title = $"C# разработчик {id}", company = "Acme" }),
        }));
    }

    private async Task Collect() => await Json(await _api.PostAsync("/api/collect", null));

    // Подтверждённые отклики в очереди, карточки давно не встречались в выдаче; DetailsLoaded считает только проверки
    private async Task<string[]> Waiting(params string[] ids)
    {
        await SaveSearch();
        await Import(ids);
        await Collect();
        foreach (var id in ids)
            Assert.Equal("Queued", await Status(id));

        await MakeStale();
        factory.Source.ResetCounters();
        return ids;
    }

    private Task MakeStale() =>
        factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""UPDATE "Vacancies" SET "LastSeenAt" = now() - interval '25 hours'"""));

    private Task SetStatus(string status) =>
        factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""UPDATE "JobApplications" SET "Status" = {0}""", status));

    private async Task<JsonElement> Vacancy(string id) =>
        (await Json(await _api.GetAsync("/api/vacancies?take=200"))).EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);

    private async Task<string?> Status(string id) => (await Vacancy(id)).GetProperty("status").GetString();

    private async Task<(string? Status, string? Reason)> Closed(string id)
    {
        var v = await Vacancy(id);
        return (v.GetProperty("status").GetString(), v.GetProperty("statusReason").GetString());
    }

    private Task<DateTimeOffset?> ArchiveChecked(string id) =>
        factory.WithDbAsync(db => db.Vacancies.Where(v => v.Id == id).Select(v => v.ArchiveCheckedAt).SingleAsync());

    [Theory]
    [InlineData("Pending")]
    [InlineData("Review")]
    [InlineData("Questionnaire")]
    [InlineData("Queued")]
    public async Task Archived_ClosesWaitingApplication_WithReasonAndHistory(string status)
    {
        await Waiting("1");
        await SetStatus(status);
        factory.Source.ArchivedPages.Add("1");

        await Collect();

        var v = await Vacancy("1");
        Assert.Equal(("Skipped", Archived), (v.GetProperty("status").GetString(), v.GetProperty("statusReason").GetString()));
        Assert.True(v.GetProperty("vacancyArchived").GetBoolean());
        var last = v.GetProperty("history").EnumerateArray().Last();
        Assert.Equal((status, "Skipped", Archived), (last.GetProperty("from").GetString(), last.GetProperty("to").GetString(), last.GetProperty("reason").GetString()));
        Assert.NotNull(await ArchiveChecked("1"));
        Assert.Equal(1, factory.Source.DetailsLoaded);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Review")]
    [InlineData("Questionnaire")]
    [InlineData("Queued")]
    public async Task NotFound_ClosesWaitingApplication_AsUnavailable(string status)
    {
        await Waiting("1");
        await SetStatus(status);
        factory.Source.NoDetails.Add("1");

        await Collect();

        Assert.Equal(("Skipped", Unavailable), await Closed("1"));
        Assert.False((await Vacancy("1")).GetProperty("vacancyArchived").GetBoolean());
    }

    [Theory]
    [InlineData("Applied")]
    [InlineData("Invited")]
    [InlineData("Rejected")]
    [InlineData("Skipped")]
    [InlineData("Failed")]
    public async Task SettledApplications_NotCheckedNotTouched(string status)
    {
        await Waiting("1");
        await SetStatus(status);
        factory.Source.ArchivedPages.Add("1");
        factory.Source.NoDetails.Add("1");

        await Collect();

        Assert.Equal(0, factory.Source.DetailsLoaded);
        Assert.Equal(status, await Status("1"));
    }

    [Fact]
    public async Task AvailablePage_KeepsApplication_AndMarksChecked()
    {
        await Waiting("1");

        await Collect();

        Assert.Equal("Queued", await Status("1"));
        Assert.NotNull(await ArchiveChecked("1"));
    }

    [Fact]
    public async Task SourceBlocked_ClosesNothing_DoneBeforeIsSaved()
    {
        await Waiting("1", "2");
        // Первой идёт вакансия с меньшим id: она успевает проверяться, вторая отвечает капчей
        factory.Source.ArchivedPages.Add("1");
        factory.Source.BlockedDetails.Add("2");

        Assert.Equal(HttpStatusCode.BadGateway, (await _api.PostAsync("/api/collect", null)).StatusCode);

        Assert.Equal(("Skipped", Archived), await Closed("1"));
        Assert.Equal("Queued", await Status("2"));
        Assert.Null(await ArchiveChecked("2"));
    }

    [Fact]
    public async Task ServerError_DoesNotClose_DoesNotStopPass_Retried()
    {
        await Waiting("1", "2");
        factory.Source.ErrorPages.Add("1");
        factory.Source.ArchivedPages.Add("2");

        await Collect();

        Assert.Equal("Queued", await Status("1"));
        Assert.Null(await ArchiveChecked("1"));
        Assert.Equal(("Skipped", Archived), await Closed("2"));

        factory.Source.ErrorPages.Clear();
        factory.Source.ResetCounters();
        await Collect();

        Assert.Equal(1, factory.Source.DetailsLoaded);
        Assert.NotNull(await ArchiveChecked("1"));
    }

    [Fact]
    public async Task Limited_PerPass_NextPassTakesOthers()
    {
        var ids = Enumerable.Range(1, 12).Select(i => i.ToString("00")).ToArray();
        await Waiting(ids);

        await Collect();

        Assert.Equal(10, factory.Source.DetailsLoaded);
        var checkedFirst = await factory.WithDbAsync(db => db.Vacancies.Where(v => v.ArchiveCheckedAt != null).Select(v => v.Id).ToListAsync());
        Assert.Equal(10, checkedFirst.Count);

        await Collect();

        Assert.Equal(12, factory.Source.DetailsLoaded);
        var checkedAll = await factory.WithDbAsync(db => db.Vacancies.Where(v => v.ArchiveCheckedAt != null).Select(v => v.Id).ToListAsync());
        Assert.Equal(ids.Order(), checkedAll.Order());
    }

    [Fact]
    public async Task RecentlySeen_NotChecked()
    {
        await Waiting("1");
        await factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""UPDATE "Vacancies" SET "LastSeenAt" = now() - interval '23 hours'"""));

        await Collect();

        Assert.Equal(0, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task CheckedRecently_NotRepeated_UntilThreshold()
    {
        await Waiting("1");
        await Collect();
        Assert.Equal(1, factory.Source.DetailsLoaded);

        await Collect();
        Assert.Equal(1, factory.Source.DetailsLoaded);

        await factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""UPDATE "Vacancies" SET "ArchiveCheckedAt" = now() - interval '25 hours'"""));
        await Collect();
        Assert.Equal(2, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task SharedVacancy_OneRequest_AllApplicationsClosed()
    {
        await Waiting("1");
        await factory.WithDbAsync(async db =>
        {
            var vacancy = await db.Vacancies.SingleAsync(v => v.Id == "1");
            var second = JobApplication.Create(vacancy, null, DateTimeOffset.UtcNow);
            second.Skip(null, DateTimeOffset.UtcNow);
            second.Requeue(DateTimeOffset.UtcNow);
            db.JobApplications.Add(second);
            return await db.SaveChangesAsync();
        });
        factory.Source.ArchivedPages.Add("1");

        await Collect();

        Assert.Equal(1, factory.Source.DetailsLoaded);
        var statuses = await factory.WithDbAsync(db => db.JobApplications.Select(a => a.StatusReason).ToListAsync());
        Assert.Equal([Archived, Archived], statuses);
    }

    [Fact]
    public async Task ConcurrentDecision_Kept_PassNotFailed_OthersNextPass()
    {
        await Waiting("1", "2");
        factory.Source.ArchivedPages.UnionWith(["1", "2"]);
        // Пока грузится страница, пользователь сам пропустил отклик: xmin отклика изменился
        factory.Source.WhileLoadingDetails = async id =>
        {
            factory.Source.WhileLoadingDetails = null;
            await Json(await _api.PostAsJsonAsync($"/api/vacancies/{id}/status", new { status = "Skipped", reason = "сам" }));
        };

        await Collect();

        Assert.Equal(("Skipped", "сам"), await Closed("1"));
        Assert.Equal("Queued", await Status("2"));

        await Collect();

        Assert.Equal(("Skipped", Archived), await Closed("2"));
        Assert.Equal(("Skipped", "сам"), await Closed("1"));
    }

    [Fact]
    public async Task NoSources_ClosesNothing()
    {
        await Waiting("1");

        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.RemoveAll<IVacancySource>()));
        var admin = app.CreateDefaultClient(new Uri("http://localhost"), new LoginHandler(ApiFactory.AdminEmail, ApiFactory.AdminPassword), new CookieContainerHandler());
        await Json(await admin.PostAsync("/api/collect", null));

        Assert.Equal("Queued", await Status("1"));
        Assert.Null(await ArchiveChecked("1"));
    }

    [Fact]
    public async Task ZeroLimit_DisablesCheck()
    {
        await Waiting("1");

        await using var app = factory.WithWebHostBuilder(b => b.UseSetting("Collector:MaxArchiveChecks", "0"));
        var admin = app.CreateDefaultClient(new Uri("http://localhost"), new LoginHandler(ApiFactory.AdminEmail, ApiFactory.AdminPassword), new CookieContainerHandler());
        await Json(await admin.PostAsync("/api/collect", null));

        Assert.Equal(0, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task ImportedCard_InArchive_ClosedWithoutLlm_OnConfirmation()
    {
        await SaveSearch();
        factory.Llm.Configured = true;
        await Import("1");
        factory.Source.ArchivedPages.Add("1");

        await Collect();

        Assert.Equal(("Skipped", Archived), await Closed("1"));
        Assert.Equal(0, factory.Llm.Calls);
        // Карточка заменена данными страницы: текст клиента дальше не идёт
        var v = await Vacancy("1");
        Assert.Equal("ASP.NET Core", v.GetProperty("description").GetString());
        Assert.False(await factory.WithDbAsync(db => db.Vacancies.Where(x => x.Id == "1").Select(x => x.Imported).SingleAsync()));
    }

    [Fact]
    public async Task ImportedCard_FoundInSearch_InArchive_Closed()
    {
        await SaveSearch();
        await Import("1");
        factory.Source.Add("1", "C# разработчик 1", "ASP.NET Core");
        factory.Source.ArchivedPages.Add("1");

        await Collect();

        Assert.Equal(("Skipped", Archived), await Closed("1"));
        Assert.Equal(1, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task ImportedCard_ServerError_KeepsWaiting_PassNotFailed()
    {
        await SaveSearch();
        await Import("1");
        factory.Source.ErrorPages.Add("1");

        await Collect();

        Assert.Equal("New", await Status("1"));

        factory.Source.ErrorPages.Clear();
        await Collect();

        Assert.Equal("Queued", await Status("1"));
    }

    [Fact]
    public async Task NewCard_InArchive_Closed_NoLlm_NotRequestedAgain()
    {
        await SaveSearch();
        factory.Llm.Configured = true;
        factory.Source.Add("1", "C# разработчик 1", "ASP.NET Core");
        factory.Source.ArchivedPages.Add("1");

        await Collect();

        Assert.Equal(("Skipped", Archived), await Closed("1"));
        Assert.Equal(0, factory.Llm.Calls);

        await Collect();

        Assert.Equal(1, factory.Source.DetailsLoaded);
    }

    [Fact]
    public async Task NewCard_ServerError_DoesNotStopPass()
    {
        await SaveSearch();
        factory.Source.Add("1", "C# разработчик 1", "ASP.NET Core");
        factory.Source.Add("2", "C# разработчик 2", "ASP.NET Core");
        factory.Source.ErrorPages.Add("1");

        await Collect();

        Assert.Equal("Queued", await Status("2"));
        Assert.Equal(0, await factory.WithDbAsync(db => db.Vacancies.CountAsync(v => v.Id == "1")));
    }

    [Fact]
    public async Task ArchivedApplication_ReturnsToQueue()
    {
        await Waiting("1");
        factory.Source.ArchivedPages.Add("1");
        await Collect();

        var v = await Json(await _api.PostAsJsonAsync("/api/vacancies/1/status", new { status = "Queued" }));

        Assert.Equal("Queued", v.GetProperty("status").GetString());
        Assert.False(v.GetProperty("vacancyArchived").GetBoolean());
    }

    [Fact]
    public async Task Collection_UpdatesLastSeen_ForKnownCard_WithoutNewApplication()
    {
        await SaveSearch();
        factory.Source.Add("1", "C# разработчик 1", "ASP.NET Core");
        await Collect();
        var before = await Vacancy("1");
        await MakeStale();
        var loaded = factory.Source.DetailsLoaded;

        await Collect();

        var after = await Vacancy("1");
        Assert.True(after.GetProperty("lastSeenAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(before.GetProperty("collectedAt").GetDateTimeOffset(), after.GetProperty("collectedAt").GetDateTimeOffset());
        Assert.Equal(loaded, factory.Source.DetailsLoaded);
        Assert.Single(await factory.WithDbAsync(db => db.JobApplications.ToListAsync()));
    }

    [Fact]
    public async Task Import_UpdatesLastSeen_ForKnownCard_WithoutNewApplication()
    {
        await SaveSearch();
        await Import("1");
        await MakeStale();

        await Json(await _api.PostAsJsonAsync("/api/import", new { search = Search, vacancies = new[] { new { id = "1", title = "x", company = "y" } } }));

        Assert.True((await Vacancy("1")).GetProperty("lastSeenAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Single(await factory.WithDbAsync(db => db.JobApplications.ToListAsync()));
    }

    // Встретилась в выдаче — страницу не проверяем: вакансия жива, и запрос к источнику не нужен
    [Fact]
    public async Task SeenInSearchThisPass_NotChecked()
    {
        await Waiting("1");
        factory.Source.Add("1", "C# разработчик 1", "ASP.NET Core");

        await Collect();

        Assert.Equal(0, factory.Source.DetailsLoaded);
    }
}
