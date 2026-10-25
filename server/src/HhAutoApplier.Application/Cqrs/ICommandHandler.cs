namespace HhAutoApplier.Application.Cqrs;

// Сценарий, меняющий состояние. Без медиатора: контроллер получает обработчик напрямую через DI
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct = default);
}
