namespace HhAutoApplier.Api.Middlewares;

// Простая защита: ключ в заголовке X-Api-Key. Пустой ключ в конфиге отключает проверку (только для разработки)
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
{
    private const string Header = "X-Api-Key";

    private readonly string? _key = config["Api:Key"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsOpen(context.Request) || string.IsNullOrEmpty(_key) || context.Request.Headers[Header] == _key)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    // Без ключа: проверка живости, preflight CORS и документация API
    private static bool IsOpen(HttpRequest request) =>
        request.Method == HttpMethods.Options
        || request.Path.StartsWithSegments("/health")
        || request.Path.StartsWithSegments("/openapi")
        || request.Path.StartsWithSegments("/scalar");
}
