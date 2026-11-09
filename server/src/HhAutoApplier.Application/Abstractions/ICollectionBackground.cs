namespace HhAutoApplier.Application.Abstractions;

// Проход сбора вне HTTP-запроса. Запуск уже занят в CollectionProgress
public interface ICollectionBackground
{
    void Run();
}
