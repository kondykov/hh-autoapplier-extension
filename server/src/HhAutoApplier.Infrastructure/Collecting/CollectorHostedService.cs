using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Commands.Collecting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Collecting;

// Расписание сбора: сразу при старте (если включено) и дальше раз в интервал. Сам сбор — CollectVacanciesCommandHandler
public class CollectorHostedService(
    IServiceScopeFactory scopes,
    IOptions<CollectorOptions> options,
    ILogger<CollectorHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var opt = options.Value;
        if (!opt.CollectOnStartup)
            await Task.Delay(TimeSpan.FromMinutes(opt.IntervalMinutes), stop);

        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<CollectVacanciesCommandHandler>();
                await handler.Handle(new CollectVacanciesCommand(), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Сбор вакансий завершился ошибкой");
            }

            await Task.Delay(TimeSpan.FromMinutes(opt.IntervalMinutes), stop);
        }
    }
}
