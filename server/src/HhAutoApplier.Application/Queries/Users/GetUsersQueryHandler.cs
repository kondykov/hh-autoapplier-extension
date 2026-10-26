using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;

namespace HhAutoApplier.Application.Queries.Users;

public class GetUsersQueryHandler(IUserService users) : IQueryHandler<GetUsersQuery, IReadOnlyList<UserDto>>
{
    public Task<IReadOnlyList<UserDto>> Handle(GetUsersQuery query, CancellationToken ct = default) => users.GetAllAsync(ct);
}