namespace HhAutoApplier.Application.Cqrs;

// Чтение без изменения состояния: AsNoTracking и проекция в DTO
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken ct = default);
}
