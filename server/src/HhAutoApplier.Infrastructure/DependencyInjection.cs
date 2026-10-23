using System.Net;
using HhAutoApplier.Infrastructure.Collecting;
using HhAutoApplier.Infrastructure.Persistence;
using HhAutoApplier.Infrastructure.Sources;
using HhAutoApplier.Infrastructure.Sources.Hh;
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

        // Источники вакансий: новый агрегатор = своя реализация IVacancySource и регистрация здесь
        var hh = config.GetSection(HhOptions.Section);
        services.Configure<HhOptions>(hh);
        if (hh.GetValue("Enabled", true))
        {
            services.AddHttpClient<HhWebSource>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
            services.AddTransient<IVacancySource>(sp => sp.GetRequiredService<HhWebSource>());
        }

        services.AddScoped<VacancyCollector>();
        services.AddHostedService<CollectorHostedService>();
        return services;
    }
}
