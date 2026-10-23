namespace HhAutoApplier.Application.Cqrs;

// Преобразование сущности домена в DTO ответа
public interface IExtractor<in TSource, out TTarget>
{
    TTarget Extract(TSource source);
}
