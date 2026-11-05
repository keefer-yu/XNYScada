using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace XNY.Scada.Mes;

/// <summary>MES服务集合的扩展方法。</summary>
public static class MesServiceCollectionExtensions
{
    /// <summary>添加SCADAMES。</summary>
    public static IServiceCollection AddScadaMes(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection(MesOptions.Section).Get<MesOptions>() ?? new MesOptions());
        services.AddHttpClient(MesClient.HttpClientName);
        services.AddSingleton<MesClient>();
        services.AddSingleton<IMesClient>(sp => sp.GetRequiredService<MesClient>());
        services.AddSingleton<MesOutboxWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<MesOutboxWorker>());
        services.AddSingleton<MesCallbackListener>();
        services.AddHostedService(sp => sp.GetRequiredService<MesCallbackListener>());
        return services;
    }
}
