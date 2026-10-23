using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Application;

public static class DependencyInjection
{
    // Обработчики команд и запросов и экстракторы регистрируются автоматически: новый класс в папке — без правок здесь
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblyOf<ApplicationAssembly>()
            .AddClasses(classes => classes.AssignableToAny(typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IExtractor<,>)))
            .AsImplementedInterfaces()
            .AsSelf()
            .WithScopedLifetime());

        services.AddSingleton(VacancyEvaluator.Default);
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}

// Метка сборки для сканирования
public sealed class ApplicationAssembly;
