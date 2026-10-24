using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Llm;

// Проверить ключ и модель коротким запросом к провайдеру
public record CheckLlmCommand;

// На .NET 11: public union CheckLlmResult(LlmCheckDto, LlmUnavailableError);
[GenerateOneOf]
public partial class CheckLlmResult : OneOfBase<LlmCheckDto, LlmUnavailableError>;
