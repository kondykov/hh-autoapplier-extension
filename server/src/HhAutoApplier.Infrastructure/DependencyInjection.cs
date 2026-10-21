using HhAutoApplier.Infrastructure.Collecting;
using HhAutoApplier.Infrastructure.Hh;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<HhOptions>(config.GetSection(HhOptions.Section));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.GetConnectionString("Default")));
        services.AddHttpClient<IVacancySource, HhApiClient>();
        services.AddScoped<VacancyCollector>();
        services.AddHostedService<CollectorHostedService>();
        return services;
    }
}
