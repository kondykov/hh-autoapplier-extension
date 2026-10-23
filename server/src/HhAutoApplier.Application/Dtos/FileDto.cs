namespace HhAutoApplier.Application.Dtos;

// Файл для скачивания: контроллер отдаёт его как есть
public record FileDto(byte[] Content, string ContentType, string FileName);
