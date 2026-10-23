namespace HhAutoApplier.Application.Errors;

// Ожидаемые неуспехи сценария — значения результата, а не исключения. В HTTP их переводит контроллер
public abstract record Error(string Message);

public sealed record NotFoundError(string Message) : Error(Message);

public sealed record ConflictError(string Message) : Error(Message);

public sealed record ValidationError(string Message) : Error(Message);

// Внешний источник недоступен: капча, блокировка, лимит
public sealed record SourceUnavailableError(string Message) : Error(Message);
