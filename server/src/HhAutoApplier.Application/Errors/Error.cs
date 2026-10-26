namespace HhAutoApplier.Application.Errors;

// Ожидаемые неуспехи сценария — значения результата, а не исключения. В HTTP их переводит контроллер
public abstract record Error(string Message);

public sealed record NotFoundError(string Message) : Error(Message);

public sealed record ConflictError(string Message) : Error(Message);

public sealed record ValidationError(string Message) : Error(Message);

// Неверный логин или пароль, пользователь заблокирован или вход временно закрыт после серии неудач
public sealed record UnauthorizedError(string Message) : Error(Message);

// Вход есть, но прав нет: например, чужие данные у не-администратора
public sealed record ForbiddenError(string Message) : Error(Message);

// Внешний источник недоступен: капча, блокировка, лимит
public sealed record SourceUnavailableError(string Message) : Error(Message);

// LLM не настроена, исчерпан дневной лимит или провайдер не отвечает
public sealed record LlmUnavailableError(string Message) : Error(Message);
