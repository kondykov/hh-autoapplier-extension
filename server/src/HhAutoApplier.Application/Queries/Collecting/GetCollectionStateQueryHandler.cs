using HhAutoApplier.Application.Commands.Collecting;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;

namespace HhAutoApplier.Application.Queries.Collecting;

public class GetCollectionStateQueryHandler(CollectionProgress progress) : IQueryHandler<GetCollectionStateQuery, CollectionStateDto>
{
    public Task<CollectionStateDto> Handle(GetCollectionStateQuery query, CancellationToken ct = default) =>
        Task.FromResult(progress.State);
}
