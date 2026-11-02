using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Llm;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Tests.Api;

// #57: письма LLM одного пользователя не влияют на письма другого
[Collection(ApiCollection.Name)]
public class LlmPerUserTests(ApiFactory factory) : IAsyncLifetime
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

    // Администратор и пользователь B — с откликом на одну и ту же вакансию, оба в очереди (LLM на сборе выключена)
    private async Task<Guid> TwoOwnersOfVacancy(params string[] ids)
    {
        var user = await Json(await _admin.PostAsJsonAsync("/api/users", new { email = "b@test.local", password = Password, role = "User" }));
        var b = factory.CreateLoggedInClient("b@test.local", Password);
        foreach (var client in new[] { _admin, b })
            await Json(await client.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#", rules = new { } }));
        foreach (var id in ids)
            factory.Source.Add(id, "C# разработчик", "ASP.NET Core");
        await Json(await _admin.PostAsync("/api/collect", null));
        return Guid.Parse(user.GetProperty("id").GetString()!);
    }

    private Task<Dictionary<(string, Guid?), string?>> Letters() =>
        factory.WithDbAsync(async db => await db.JobApplications.ToDictionaryAsync(a => (a.VacancyId, a.UserId), a => a.CoverLetter));

    private Task<Guid> AdminId() => factory.WithDbAsync(async db => (await db.Users.SingleAsync(u => u.Email == ApiFactory.AdminEmail)).Id);

    [Fact]
    public async Task GivenUpLetter_IsPerApplication_NotPerVacancy()
    {
        var b = await TwoOwnersOfVacancy("1");
        var admin = await AdminId();
        // У администратора письмо по вакансии дважды вышло с цифрами не из фактов — сутки не переписывается
        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < 2; i++)
                db.LlmCalls.Add(LlmCall.Rejected(CoverLetterWriting.Purpose, "1", "fake-model", "промпт", "письмо", $"{CoverLetterWriting.FiguresError}: 10",
                    100, 20, DateTimeOffset.UtcNow).For(admin));
            return await db.SaveChangesAsync();
        });

        factory.Llm.Configured = true;
        factory.Llm.Answers.Enqueue("Здравствуйте! Письмо под вакансию без выдуманных фактов.");
        var res = await Json(await _admin.PostAsJsonAsync("/api/llm/letters", new { limit = 10 }));

        // Пользователь B письмо получил, хотя у администратора по этой вакансии LLM «сдалась»
        Assert.Equal(1, res.GetProperty("written").GetInt32());
        var letters = await Letters();
        Assert.Null(letters[("1", admin)]);
        Assert.NotNull(letters[("1", b)]);
        // Вызов записан на владельца отклика
        var owners = await factory.WithDbAsync(db => db.LlmCalls.Where(c => c.Error == null).Select(c => c.UserId).ToListAsync());
        Assert.Equal([b], owners);
    }

    [Fact]
    public async Task ResetLetters_OwnByDefault_OtherUserByQuery_ManualOnlyExplicitly()
    {
        var b = await TwoOwnersOfVacancy("1", "2");
        var admin = await AdminId();
        await factory.WithDbAsync(async db =>
        {
            foreach (var a in await db.JobApplications.ToListAsync())
            {
                var manual = a.UserId == admin && a.VacancyId == "2";
                a.SetCoverLetter($"Письмо {a.VacancyId}", manual ? JobApplication.ManualLetter : "fake-model", DateTimeOffset.UtcNow);
            }

            return await db.SaveChangesAsync();
        });

        var own = await Json(await _admin.PostAsJsonAsync("/api/llm/letters/reset", new { }));
        Assert.Equal(1, own.GetProperty("reset").GetInt32());
        var letters = await Letters();
        Assert.Null(letters[("1", admin)]);
        // Письмо, поправленное человеком, и письма другого пользователя — на месте
        Assert.Equal("Письмо 2", letters[("2", admin)]);
        Assert.Equal(("Письмо 1", "Письмо 2"), (letters[("1", b)], letters[("2", b)]));

        var other = await Json(await _admin.PostAsJsonAsync($"/api/llm/letters/reset?userId={b}", new { vacancyIds = new[] { "2" } }));
        Assert.Equal(1, other.GetProperty("reset").GetInt32());
        letters = await Letters();
        Assert.Equal("Письмо 1", letters[("1", b)]);
        Assert.Null(letters[("2", b)]);

        var manual = await Json(await _admin.PostAsJsonAsync("/api/llm/letters/reset", new { includeManual = true }));
        Assert.Equal(1, manual.GetProperty("reset").GetInt32());
        Assert.Null((await Letters())[("2", admin)]);
    }
}
