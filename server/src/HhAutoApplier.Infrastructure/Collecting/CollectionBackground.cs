using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Commands.Collecting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Infrastructure.Collecting;

// Проход сбора со своим scope и токеном остановки приложения: закрытая страница и оборванный запрос его не отменяют
public class CollectionBackground(
    IServiceScopeFactory scopes,
    CollectionProgress progress,
    IHostApplicationLifetime lifetime,
    ILogger<CollectionBackground> logger
) : ICollectionBackground
{
    public void Run() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var stop = lifetime.ApplicationStopping;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var collect = scope.ServiceProvider.GetRequiredService<CollectVacanciesCommandHandler>();
            await collect.Handle(new CollectVacanciesCommand(CollectionProgress.Button, Reserved: true), stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Сервер останавливается: состояние записал обработчик
        }
        catch (Exception e)
        {
            logger.LogError(e, "Сбор вакансий завершился ошибкой");
            // Обработчик мог не успеть начать работу: иначе запуск останется занятым до перезапуска
            progress.Fail(CollectVacanciesCommandHandler.FailedMessage);
        }
    }
}
