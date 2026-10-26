using System.Net;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Infrastructure.Collecting;
using HhAutoApplier.Infrastructure.Identity;
using HhAutoApplier.Infrastructure.Llm;
using HhAutoApplier.Infrastructure.Persistence;
using HhAutoApplier.Infrastructure.Sources.Hh;
using HhAutoApplier.Infrastructure.Telegram;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CollectorOptions>(config.GetSection(CollectorOptions.Section));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.GetConnectionString("Default")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Пользователи и роли: ASP.NET Core Identity поверх той же базы. Схемы входа (cookie, ключ API) — в Api
        services.Configure<AdminOptions>(config.GetSection(AdminOptions.Section));
        services.AddIdentityCore<AppUser>(o =>
            {
                // Длина важнее набора классов символов; пароль задаёт человек, не генератор
                o.Password.RequiredLength = 10;
                o.Password.RequiredUniqueChars = 4;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager<AppSignInManager>()
            .AddErrorDescriber<RussianIdentityErrorDescriber>();
        services.AddScoped<IUserService, UserService>();

        // Источники вакансий: новый агрегатор = своя реализация IVacancySource и регистрация здесь
        var hh = config.GetSection(HhOptions.Section);
        services.Configure<HhOptions>(hh);
        if (hh.GetValue("Enabled", true))
        {
            services.AddHttpClient<HhWebSource>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
            services.AddTransient<IVacancySource>(sp => sp.GetRequiredService<HhWebSource>());
        }

        // LLM: OpenAI-совместимый провайдер (по умолчанию Groq); без ключа IsConfigured = false
        services.Configure<LlmOptions>(config.GetSection(LlmOptions.Section));
        services.AddSingleton(sp => new LlmBudget(sp.GetRequiredService<TimeProvider>(), day =>
        {
            using var scope = sp.CreateScope();
            var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmCalls.Count(c => c.CreatedAt >= from && c.CreatedAt < from.AddDays(1));
        }));
        services.AddHttpClient<ILlmClient, OpenAiCompatibleClient>();

        // Telegram: без токена бот выключен, уведомления не пишутся
        services.Configure<TelegramOptions>(config.GetSection(TelegramOptions.Section));
        // Логи этого клиента — только от Warning (appsettings): в адресе запроса Bot API — токен бота
        services.AddHttpClient(nameof(TelegramBot));
        services.AddSingleton<TelegramBot>();
        services.AddSingleton<ITelegramBot>(sp => sp.GetRequiredService<TelegramBot>());
        services.AddHostedService<TelegramHostedService>();

        services.AddHostedService<CollectorHostedService>();
        return services;
    }
}
