using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.Modules.Identity.Infrastructure.Auth;
using StockOrder.Modules.Identity.Infrastructure.Authorization;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<IdentityDbContext>(
            (sp, options) => new IdentityDbContext(options, sp.GetRequiredService<ICurrentUserContext>()));

        services.AddScoped<IPermissionLoader, PermissionLoader>();
        services.AddScoped<IAccessScope, UserAccessScope>();
        services.AddSingleton<IMfaSecretProtector, MfaSecretProtector>();

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}
