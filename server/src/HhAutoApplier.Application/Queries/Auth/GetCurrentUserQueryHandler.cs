using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;

namespace HhAutoApplier.Application.Queries.Auth;

public class GetCurrentUserQueryHandler(ICurrentUser current) : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public Task<CurrentUserDto> Handle(GetCurrentUserQuery query, CancellationToken ct = default) =>
        Task.FromResult(new CurrentUserDto { Id = current.Id, Name = current.Name, Role = current.Role });
}