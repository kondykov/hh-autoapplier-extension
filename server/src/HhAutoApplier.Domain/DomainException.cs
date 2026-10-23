namespace HhAutoApplier.Domain;

// Нарушено правило домена: недопустимый переход статуса, пустой обязательный атрибут
public class DomainException(string message) : Exception(message);
