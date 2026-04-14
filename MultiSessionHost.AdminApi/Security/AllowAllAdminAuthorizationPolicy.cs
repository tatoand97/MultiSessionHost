using Microsoft.AspNetCore.Http;

namespace MultiSessionHost.AdminApi.Security;

public sealed class AllowAllAdminAuthorizationPolicy : IAdminAuthorizationPolicy
{
    public Task<bool> IsAuthorizedAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
