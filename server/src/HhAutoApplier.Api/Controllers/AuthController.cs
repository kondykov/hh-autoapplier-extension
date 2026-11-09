using HhAutoApplier.Application.Commands.Auth;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Вход")]
[Route("api/auth")]
[Authorize]
public class AuthController : ApiController
{
    /// <summary>
    /// Вход по email и паролю: в ответе — cookie сессии на 14 дней (продлевается при работе).
    /// После 5 неудачных попыток вход закрыт на 15 минут.
    /// </summary>
    /// <response code="401">Неверный email или пароль, пользователь заблокирован или вход временно закрыт.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UnauthorizedError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginCommand command,
        [FromServices] LoginCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Выйти: cookie сессии удаляется.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromServices] LogoutCommandHandler handler, CancellationToken ct)
    {
        return FromResult(await handler.Handle(new LogoutCommand(), ct));
    }

    /// <summary>
    /// Кто выполняет запрос: пользователь после входа или персональный токен (viaApiKey = true, всегда роль User).
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me([FromServices] GetCurrentUserQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetCurrentUserQuery(), ct));
    }

    /// <summary>
    /// Сменить свой пароль. Остальные сессии пользователя обрываются, текущая остаётся.
    /// </summary>
    /// <response code="400">Неверный текущий пароль или новый не проходит политику (не короче 10 символов).</response>
    /// <response code="409">Вход по токену — пароля нет.</response>
    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand command,
        [FromServices] ChangePasswordCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }
}