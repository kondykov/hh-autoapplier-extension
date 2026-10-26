using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Commands.Users;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Users;
using HhAutoApplier.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Пользователи")]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController : ApiController
{
    /// <summary>
    /// Все пользователи с ролями.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromServices] GetUsersQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetUsersQuery(), ct));
    }

    /// <summary>
    /// Завести пользователя с временным паролем (не короче 10 символов) и ролью Admin или User. Самостоятельной регистрации нет.
    /// </summary>
    /// <response code="400">Email занят, пароль не проходит политику или роль неизвестна.</response>
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserCommand command,
        [FromServices] CreateUserCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Сменить роль. Сессии пользователя обрываются — новая роль действует со следующего входа.
    /// </summary>
    /// <response code="404">Пользователь не найден.</response>
    /// <response code="409">Последний администратор.</response>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRole(
        Guid id,
        [FromBody] SetUserRoleRequest request,
        [FromServices] SetUserRoleCommandHandler handler,
        CancellationToken ct
    )
    {
        var command = new SetUserRoleCommand { UserId = id, Role = request.Role };
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Заблокировать (вход закрыт, сессии обрываются в течение минуты) или разблокировать.
    /// </summary>
    /// <response code="404">Пользователь не найден.</response>
    /// <response code="409">Нельзя заблокировать себя или последнего администратора.</response>
    [HttpPut("{id:guid}/blocked")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetBlocked(
        Guid id,
        [FromBody] SetUserBlockedRequest request,
        [FromServices] SetUserBlockedCommandHandler handler,
        CancellationToken ct
    )
    {
        var command = new SetUserBlockedCommand { UserId = id, Blocked = request.Blocked };
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Задать новый временный пароль; сессии пользователя обрываются, временная блокировка входа снимается.
    /// </summary>
    /// <response code="400">Пароль не проходит политику.</response>
    /// <response code="404">Пользователь не найден.</response>
    [HttpPut("{id:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        [FromBody] ResetUserPasswordRequest request,
        [FromServices] ResetUserPasswordCommandHandler handler,
        CancellationToken ct
    )
    {
        var command = new ResetUserPasswordCommand { UserId = id, Password = request.Password };
        return FromResult((await handler.Handle(command, ct)).Value);
    }
}

public record SetUserRoleRequest([Required] string Role);

public record SetUserBlockedRequest(bool Blocked);

public record ResetUserPasswordRequest([Required, MaxLength(128)] string Password);