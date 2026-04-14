using Microsoft.AspNetCore.Http;

namespace MultiSessionHost.AdminApi.Security;

public interface IAdminAuthorizationPolicy
{
    Task<bool> IsAuthorizedAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
