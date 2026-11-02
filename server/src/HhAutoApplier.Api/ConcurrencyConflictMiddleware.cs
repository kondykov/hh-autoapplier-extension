using HhAutoApplier.Application.Errors;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Api;

// Отклик изменился между чтением и записью (#54: LLM-конвейер или другая вкладка успели раньше) — 409, а не 500:
// клиенту обновить и повторить
public class ConcurrencyConflictMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DbUpdateConcurrencyException) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ConflictError("Отклик изменился, пока вы его смотрели, — обновите страницу и повторите"));
        }
    }
}
