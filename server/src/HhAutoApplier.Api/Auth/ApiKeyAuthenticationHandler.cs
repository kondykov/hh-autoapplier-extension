using System.Security.Claims;
using System.Text.Encodings.Web;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Api.Auth;

// Вход по персональному токену `hh_…` в заголовке X-Api-Key (#10) — так работает расширение. Общий ключ API_KEY убран в #42:
// утёкший ключ не отозвать, а токен — отзывается и принадлежит одному пользователю.
// Токен действует от имени владельца, но всегда с ролью User: по нему нельзя управлять пользователями, запускать сбор и LLM
// и читать чужие данные — утёкший токен даёт не больше, чем расширение
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    public const string Header = "X-Api-Key";

    public const string Name = "api-key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var provided))
            return AuthenticateResult.NoResult();

        return ApiToken.IsToken(provided.ToString())
            ? await AuthenticateTokenAsync(provided.ToString())
            : AuthenticateResult.Fail("Неверный токен: нужен персональный токен hh_… (POST /api/tokens после входа по паролю)");
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
}

public static class AuthClaims
{
    // Способ входа: api-key (токен); у cookie входа claim нет
    public const string Method = "hh:auth";
}