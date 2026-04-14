using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MultiSessionHost.AdminApi.Security;

namespace MultiSessionHost.AdminApi;

public static class AdminApiServiceCollectionExtensions
{
    public static IServiceCollection AddAdminApiServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRouting();
        services.TryAddSingleton<IAdminAuthorizationPolicy, AllowAllAdminAuthorizationPolicy>();

        return services;
    }
}
