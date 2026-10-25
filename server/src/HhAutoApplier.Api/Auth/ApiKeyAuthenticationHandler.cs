using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Api.Auth;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    // Общий ключ из Api:Key (API_KEY в .env); пусто — вход по ключу выключен, остаётся вход по паролю
    public string? Key { get; set; }

    // Чей ключ: email пользователя — Api:KeyUser (API_KEY_USER), а если пусто — первый администратор Admin:Email (ADMIN_EMAIL)
    public string? User { get; set; }
}

// Вход по общему ключу в заголовке X-Api-Key — так работает расширение до персональных токенов (#10). Владелец задан явно:
// ключ не переходит к другому администратору, если владельца заблокировали или понизили.
// Ключ действует от имени пользователя, но всегда с ролью User: по нему нельзя управлять пользователями, запускать сбор и LLM
// и читать чужие данные — утёкший ключ даёт не больше, чем расширение. Пользователей ещё нет — данные владельца по умолчанию.
// В #10 меняется только поиск владельца: токен из базы вместо Api:Key
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    public const string Header = "X-Api-Key";

    public const string Name = "api-key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var provided))
            return AuthenticateResult.NoResult();

        if (ApiToken.IsToken(provided.ToString()))
            return await AuthenticateTokenAsync(provided.ToString());

        if (string.IsNullOrEmpty(Options.Key) || !Matches(provided.ToString(), Options.Key))
            return AuthenticateResult.Fail("Неверный ключ API");

        var users = Context.RequestServices.GetRequiredService<IUserService>();
        var claims = new List<Claim> { new(ClaimTypes.Role, Roles.User), new(AuthClaims.Method, Name) };
        var owner = string.IsNullOrWhiteSpace(Options.User) ? null : await users.FindByEmailAsync(Options.User.Trim(), Context.RequestAborted);

        if (owner is { Blocked: false })
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString()));
            claims.Add(new Claim(ClaimTypes.Name, owner.Email));
        }
        else if (owner is not null || await users.AnyAsync(Context.RequestAborted))
        {
            // Владелец ключа заблокирован, не найден или не задан — ключ не действует, а не переходит к чужим данным
            return AuthenticateResult.Fail("Владелец ключа API не найден или заблокирован");
        }
        else
        {
            // Пользователей нет совсем: данные владельца по умолчанию, как до #7
            claims.Add(new Claim(ClaimTypes.Name, Name));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    // Персональный токен (#10): от имени его владельца, тоже с ролью User. Отозван, владелец заблокирован или удалён — 401.
    // Ищется по хэшу: открытое значение в базе не хранится
    private async Task<AuthenticateResult> AuthenticateTokenAsync(string value)
    {
        var hash = ApiToken.HashOf(value);
        var db = Context.RequestServices.GetRequiredService<IAppDbContext>();
        var token = await db.ApiTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Hash == hash && t.RevokedAt == null, Context.RequestAborted);
        var owner = token is null ? null : await Context.RequestServices.GetRequiredService<IUserService>().FindAsync(token.UserId, Context.RequestAborted);
        if (owner is not { Blocked: false })
            return AuthenticateResult.Fail("Токен недействителен");

        Claim[] claims =
        [
            new(ClaimTypes.Role, Roles.User), new(AuthClaims.Method, Name),
            new(ClaimTypes.NameIdentifier, owner.Id.ToString()), new(ClaimTypes.Name, owner.Email),
        ];
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    // Сравнение за постоянное время: по времени ответа ключ не подобрать
    private static bool Matches(string provided, string key) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(key));
}

public static class AuthClaims
{
    // Способ входа: api-key; у cookie входа claim нет
    public const string Method = "hh:auth";
}