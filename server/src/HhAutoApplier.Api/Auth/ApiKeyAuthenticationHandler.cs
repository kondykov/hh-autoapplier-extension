using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using HhAutoApplier.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Api.Auth;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    // Общий ключ из Api:Key (API_KEY в .env); пусто — вход по ключу выключен, остаётся вход по паролю
    public string? Key { get; set; }
}

// Вход по общему ключу в заголовке X-Api-Key: права Admin без учётной записи. Так работает расширение до персональных
// токенов (#10) — тогда меняется только поиск ключа: токен из базы вместо Api:Key, и принципал — его владелец
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    public const string Header = "X-Api-Key";

    public const string Name = "api-key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var provided))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (string.IsNullOrEmpty(Options.Key) || !Matches(provided.ToString(), Options.Key))
            return Task.FromResult(AuthenticateResult.Fail("Неверный ключ API"));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, Name), new Claim(ClaimTypes.Role, Roles.Admin)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    // Сравнение за постоянное время: по времени ответа ключ не подобрать
    private static bool Matches(string provided, string key) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(key));
}