using System.Security.Claims;
using HhAutoApplier.Application.Abstractions;

namespace HhAutoApplier.Api.Auth;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string Name => Principal?.Identity?.Name ?? "";

    public string Role => Principal?.FindFirstValue(ClaimTypes.Role) ?? "";

    public bool ViaApiKey => Principal?.FindFirstValue(AuthClaims.Method) == ApiKeyAuthenticationHandler.Name;
}