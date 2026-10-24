using System.Net;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Infrastructure.Collecting;
using HhAutoApplier.Infrastructure.Llm;
using HhAutoApplier.Infrastructure.Persistence;
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
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

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
        services.AddSingleton<LlmBudget>();
        services.AddHttpClient<ILlmClient, OpenAiCompatibleClient>();

        services.AddHostedService<CollectorHostedService>();
        return services;
    }
}
