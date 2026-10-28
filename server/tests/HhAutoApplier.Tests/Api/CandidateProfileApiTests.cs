using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Llm;

namespace HhAutoApplier.Tests.Api;

// Профиль кандидата (#35): свой профиль через API, чужой — только администратору, факты — в промпте оценки
[Collection(ApiCollection.Name)]
public class CandidateProfileApiTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _admin = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Profile(string general, params (string Skill, string Facts)[] skills) =>
        new { general, skills = skills.Select(s => new { skill = s.Skill, facts = s.Facts }).ToArray() };

    [Fact]
    public async Task Profile_EmptyThenSaved_ValidatedAndOwnOnly()
    {
        var empty = await Json(await _admin.GetAsync("/api/profile"));
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("general").ValueKind);
        Assert.Equal(0, empty.GetProperty("skills").GetArrayLength());

        var saved = await Json(await _admin.PutAsJsonAsync("/api/profile", Profile("Middle, 5 лет", ("EF Core", "N+1, AsSplitQuery"), ("", ""))));
        Assert.Equal("Middle, 5 лет", saved.GetProperty("general").GetString());
        Assert.Equal("EF Core", saved.GetProperty("skills")[0].GetProperty("skill").GetString());
        Assert.Equal(1, (await Json(await _admin.GetAsync("/api/profile"))).GetProperty("skills").GetArrayLength());

        // Навык без фактов — 400, сохранённое не меняется
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync("/api/profile", Profile("x", ("Docker", "")))).StatusCode);
        Assert.Equal("Middle, 5 лет", (await Json(await _admin.GetAsync("/api/profile"))).GetProperty("general").GetString());

        // Пользователь видит свой (пустой) профиль, чужой — 403; администратор читает его по userId
        var user = await Json(await _admin.PostAsJsonAsync("/api/users", new { email = "user@test.local", password = "user-password-1", role = "User" }));
        var userClient = factory.CreateLoggedInClient("user@test.local", "user-password-1");
        Assert.Equal(0, (await Json(await userClient.GetAsync("/api/profile"))).GetProperty("skills").GetArrayLength());
        var adminId = (await Json(await _admin.GetAsync("/api/auth/me"))).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync($"/api/profile?userId={adminId}")).StatusCode);
        await Json(await userClient.PutAsJsonAsync("/api/profile", Profile("Junior")));
        Assert.Equal("Junior", (await Json(await _admin.GetAsync($"/api/profile?userId={user.GetProperty("id").GetString()}"))).GetProperty("general").GetString());
        Assert.Equal("Middle, 5 лет", (await Json(await _admin.GetAsync("/api/profile"))).GetProperty("general").GetString());
    }

    [Fact]
    public async Task Evaluation_PromptHasProfileFactsForVacancy()
    {
        factory.Llm.Configured = true;
        await Json(await _admin.PutAsJsonAsync("/api/profile", Profile("Middle, 5 лет", ("EF Core", "N+1, AsSplitQuery"), ("Kafka", "консьюмеры"))));
        await Json(await _admin.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#" }));
        await Json(await _admin.PostAsJsonAsync("/api/import", new
        {
            search = "dotnet",
            vacancies = new[] { new { id = "1", title = "C# разработчик", company = "Acme", description = "ASP.NET Core, EF Core", salaryCurrency = "RUR", workFormat = "Удалённо" } },
        }));
        factory.Llm.Answers.Enqueue("""{"verdict": "fit", "score": 80, "resume": null, "reason": "подходит"}""");
        await Json(await _admin.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));

        var prompt = VacancyAssessment.Serialize(Assert.Single(factory.Llm.Requests));
        Assert.Contains("Middle, 5 лет", prompt);
        Assert.Contains("N+1, AsSplitQuery", prompt);
        Assert.DoesNotContain("консьюмеры", prompt);
    }
}
