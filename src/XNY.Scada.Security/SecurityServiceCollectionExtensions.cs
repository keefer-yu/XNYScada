using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XNY.Scada.Security;

/// <summary>安全服务集合的扩展方法。</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>添加SCADA安全。</summary>
    public static IServiceCollection AddScadaSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection(SecurityOptions.Section).Get<SecurityOptions>() ?? new SecurityOptions());
        services.AddSingleton<UserSession>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<UserSession>());
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IUserAdminService, UserAdminService>();
        services.AddSingleton<SecuritySeeder>();
        services.AddSingleton<IdleLogoutMonitor>();
        services.AddSingleton<ICardReader, SerialCardReader>();
        return services;
    }
}
