using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Vacancies;

// Проверка отклика с просьбами работодателя (#72): подтвердить кодовые слова и подставить значения из профиля
public record ResolveLetterRequestsCommand
{
    [Required]
    public required string VacancyId { get; init; }

    // Номера просьб с кодовым словом, подтверждённых человеком; пусто — только обновить значения из профиля
    [MaxLength(10)]
    public List<int> ConfirmCodeWords { get; init; } = [];
}

// На .NET 11: public union ResolveLetterRequestsResult(VacancyDto, NotFoundError, ConflictError);
[GenerateOneOf]
public partial class ResolveLetterRequestsResult : OneOfBase<VacancyDto, NotFoundError, ConflictError>;
