using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HhAutoApplier.Tests.Api;

// Персональные токены (#10): выпуск после входа по паролю, данные владельца с ролью User, отзыв и блокировка — 401
[Collection(ApiCollection.Name)]
public class TokenTests(ApiFactory factory) : IAsyncLifetime
{
    private const string UserPassword = "user-password-1";

    private readonly HttpClient _admin = factory.CreateApiClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private HttpClient WithToken(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", token);
        return client;
    }

    private async Task<(string Id, HttpClient User)> CreateUser(string email)
    {
        var user = await Json(await _admin.PostAsJsonAsync("/api/users", new { email, password = UserPassword, role = "User" }));
        return (user.GetProperty("id").GetString()!, factory.CreateLoggedInClient(email, UserPassword));
    }

    private static async Task<(string Id, string Value)> Issue(HttpClient client, string name = "расширение")
    {
        var token = await Json(await client.PostAsJsonAsync("/api/tokens", new { name }));
        return (token.GetProperty("id").GetString()!, token.GetProperty("value").GetString()!);
    }

    [Fact]
    public async Task Token_ActsAsOwnerWithUserRole_ListedWithoutValue()
    {
        var (_, user) = await CreateUser("user@test.local");
        var (id, value) = await Issue(user);
        Assert.StartsWith("hh_", value);

        var byToken = WithToken(value);
        var me = await Json(await byToken.GetAsync("/api/auth/me"));
        Assert.Equal("user@test.local", me.GetProperty("name").GetString());
        Assert.Equal("User", me.GetProperty("role").GetString());
        Assert.True(me.GetProperty("viaApiKey").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await byToken.GetAsync("/api/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await byToken.GetAsync("/api/users")).StatusCode);

        // Токеном новый токен не выпустить
        Assert.Equal(HttpStatusCode.Conflict, (await byToken.PostAsJsonAsync("/api/tokens", new { name = "ещё" })).StatusCode);

        var listed = (await Json(await user.GetAsync("/api/tokens"))).EnumerateArray().Single();
        Assert.Equal(id, listed.GetProperty("id").GetString());
        Assert.False(listed.TryGetProperty("value", out _));
        Assert.StartsWith(listed.GetProperty("prefix").GetString()!, value);
    }

    [Fact]
    public async Task Revoked_Or_Unknown_Or_OwnerBlocked_401()
    {
        var (userId, user) = await CreateUser("user@test.local");
        var (id, value) = await Issue(user);
        var (_, other) = await Issue(user, "второй");

        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken("hh_unknown").GetAsync("/api/stats")).StatusCode);

        // Чужой токен не отозвать
        var (_, stranger) = await CreateUser("stranger@test.local");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/tokens/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync($"/api/tokens/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(value).GetAsync("/api/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await user.DeleteAsync($"/api/tokens/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await WithToken(other).GetAsync("/api/stats")).StatusCode);
        await Json(await _admin.PutAsJsonAsync($"/api/users/{userId}/blocked", new { blocked = true }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(other).GetAsync("/api/stats")).StatusCode);
    }

    [Fact]
    public async Task Token_SeesOnlyOwnersData()
    {
        var (_, user) = await CreateUser("user@test.local");
        await Json(await user.PutAsJsonAsync("/api/searches", new { name = "mine", query = "C#" }));
        await Json(await _admin.PutAsJsonAsync("/api/searches", new { name = "admins", query = "PHP" }));
        var (_, value) = await Issue(user);

        var names = (await Json(await WithToken(value).GetAsync("/api/searches"))).EnumerateArray().Select(s => s.GetProperty("name").GetString());
        Assert.Equal(["mine"], names);
    }
}