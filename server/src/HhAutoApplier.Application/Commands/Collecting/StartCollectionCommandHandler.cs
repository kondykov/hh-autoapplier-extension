using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Errors;

namespace HhAutoApplier.Application.Commands.Collecting;

public class StartCollectionCommandHandler(
    CollectionProgress progress,
    ICollectionBackground background
) : ICommandHandler<StartCollectionCommand, StartCollectionResult>
{
    public Task<StartCollectionResult> Handle(StartCollectionCommand command, CancellationToken ct = default)
    {
        if (!progress.TryBegin(CollectionProgress.Button))
            return Task.FromResult<StartCollectionResult>(new ConflictError("Сбор уже идёт"));

        background.Run();
        return Task.FromResult<StartCollectionResult>(progress.State);
    }
}
