using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Tests.Api;

// Вход по паролю (cookie) и по общему ключу API, роли и управление пользователями
[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory) : IAsyncLifetime
{
    private const string UserPassword = "user-password-1";

    private readonly HttpClient _api = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Клиент без ключа: cookie сессии сохраняются между запросами
    private async Task<(HttpClient Client, HttpResponseMessage Response)> Login(string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        return (client, response);
    }

    private async Task<HttpClient> LoggedIn(string email, string password)
    {
        var (client, response) = await Login(email, password);
        await Json(response);
        return client;
    }

    private async Task<string> CreateUser(string email, string role = "User", string password = UserPassword)
    {
        var user = await Json(await _api.PostAsJsonAsync("/api/users", new { email, password, role }));
        return user.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Login_CookieSession_MeAndLogout()
    {
        var admin = await LoggedIn(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var me = await Json(await admin.GetAsync("/api/auth/me"));
        Assert.Equal(ApiFactory.AdminEmail, me.GetProperty("name").GetString());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        Assert.NotEqual(JsonValueKind.Null, me.GetProperty("id").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/stats")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/stats")).StatusCode);
    }

    [Fact]
    public async Task Login_WrongPasswordOrUnknownEmail_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(ApiFactory.AdminEmail, "wrong-password")).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("nobody@test.local", ApiFactory.AdminPassword)).Response.StatusCode);
    }

    [Fact]
    public async Task Login_LockedAfterFiveFailures()
    {
        for (var i = 0; i < 5; i++)
            await Login(ApiFactory.AdminEmail, "wrong-password");

        var (_, response) = await Login(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("попыток", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiKey_ActsAsAdminWithoutAccount_WrongKeyRejected()
    {
        var me = await Json(await _api.GetAsync("/api/auth/me"));
        Assert.Equal("api-key", me.GetProperty("name").GetString());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("id").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await _api.PostAsJsonAsync("/api/auth/password", new { currentPassword = "x", newPassword = "y" })).StatusCode);

        var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Api-Key", "not-the-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/api/stats")).StatusCode);
    }

    [Fact]
    public async Task UserRole_OnlyOwnAccount_NoSharedDataOrUsers()
    {
        await CreateUser("user@test.local");
        var user = await LoggedIn("user@test.local", UserPassword);

        Assert.Equal("User", (await Json(await user.GetAsync("/api/auth/me"))).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task CreateUser_Validation()
    {
        await CreateUser("user@test.local");
        var users = await Json(await _api.GetAsync("/api/users"));
        Assert.Equal([ApiFactory.AdminEmail, "user@test.local"], users.EnumerateArray().Select(u => u.GetProperty("email").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsJsonAsync("/api/users", new { email = "user@test.local", password = UserPassword, role = "User" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsJsonAsync("/api/users", new { email = "short@test.local", password = "short", role = "User" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsJsonAsync("/api/users", new { email = "role@test.local", password = UserPassword, role = "Owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsJsonAsync("/api/users", new { email = "not-an-email", password = UserPassword, role = "User" })).StatusCode);
    }

    [Fact]
    public async Task Block_ClosesLoginAndSession_UnblockReopens()
    {
        var id = await CreateUser("user@test.local");
        var session = await LoggedIn("user@test.local", UserPassword);

        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/auth/me")).StatusCode);

        var blocked = await Json(await _api.PutAsJsonAsync($"/api/users/{id}/blocked", new { blocked = true }));
        Assert.True(blocked.GetProperty("blocked").GetBoolean());
        // В тестах штамп безопасности проверяется на каждом запросе (в бою — раз в минуту): сессия оборвана
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        var (_, login) = await Login("user@test.local", UserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("заблокирован", await login.Content.ReadAsStringAsync());

        // После разблокировки прежняя сессия не оживает — нужен новый вход
        await Json(await _api.PutAsJsonAsync($"/api/users/{id}/blocked", new { blocked = false }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        await LoggedIn("user@test.local", UserPassword);
    }

    // Блокировка проверяется при каждой проверке сессии, а не только через смену штампа: иначе заблокированный успел бы
    // перевыпустить себе cookie (смена пароля) до проверки штампа и остаться в системе
    [Fact]
    public async Task Blocked_SessionRejectedEvenWithSameSecurityStamp()
    {
        await CreateUser("user@test.local");
        var session = await LoggedIn("user@test.local", UserPassword);

        await factory.WithDbAsync(db => db.Users.Where(u => u.Email == "user@test.local")
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Blocked, true)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.PostAsJsonAsync("/api/auth/password", new { currentPassword = UserPassword, newPassword = "new-password-1" })).StatusCode);
        var (_, wrong) = await Login("user@test.local", "wrong-password");
        Assert.DoesNotContain("заблокирован", await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LastAdmin_CannotBeDemotedOrBlocked_SelfBlockForbidden()
    {
        var admins = await Json(await _api.GetAsync("/api/users"));
        var adminId = admins.EnumerateArray().Single().GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Conflict, (await _api.PutAsJsonAsync($"/api/users/{adminId}/role", new { role = "User" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _api.PutAsJsonAsync($"/api/users/{adminId}/blocked", new { blocked = true })).StatusCode);

        // Со вторым администратором первого можно понизить, но себя заблокировать нельзя
        var second = await CreateUser("second@test.local", "Admin");
        var admin = await LoggedIn(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{adminId}/blocked", new { blocked = true })).StatusCode);
        Assert.Equal("User", (await Json(await _api.PutAsJsonAsync($"/api/users/{second}/role", new { role = "user" }))).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.PutAsJsonAsync($"/api/users/{Guid.NewGuid()}/role", new { role = "User" })).StatusCode);
    }

    [Fact]
    public async Task Passwords_ChangeOwnAndResetByAdmin()
    {
        var id = await CreateUser("user@test.local");
        var user = await LoggedIn("user@test.local", UserPassword);

        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/auth/password", new { currentPassword = "wrong-password", newPassword = "new-password-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsJsonAsync("/api/auth/password", new { currentPassword = UserPassword, newPassword = "new-password-1" })).StatusCode);
        // Текущая сессия перевыпущена и работает
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("user@test.local", UserPassword)).Response.StatusCode);
        await LoggedIn("user@test.local", "new-password-1");

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsJsonAsync($"/api/users/{id}/password", new { password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PutAsJsonAsync($"/api/users/{id}/password", new { password = "reset-password-1" })).StatusCode);
        await LoggedIn("user@test.local", "reset-password-1");
    }
}