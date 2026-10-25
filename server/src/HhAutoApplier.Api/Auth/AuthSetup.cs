using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace HhAutoApplier.Api.Auth;

public static class AuthSetup
{
    private const string Selector = "CookieOrApiKey";

    // Два способа войти: cookie после входа по паролю (страницы backend) и заголовок X-Api-Key (расширение).
    // Без атрибутов эндпоинт доступен только Admin: данные пока общие и принадлежат владельцу, свои — после #9
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(Selector)
            .AddPolicyScheme(Selector, null, o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.Header)
                    ? ApiKeyAuthenticationHandler.SchemeName
                    : IdentityConstants.ApplicationScheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, o => o.Key = config["Api:Key"])
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "hh_auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            // HTTP только на 127.0.0.1; снаружи — TLS (#6)
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            // API, а не сайт: вместо перенаправления на страницу входа — код ответа
            o.Events.OnRedirectToLogin = context => Status(context, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden);
        });

        // Заблокированный или сменивший роль и пароль пользователь теряет сессию не позже чем через минуту
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireRole(Roles.Admin).Build());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}