using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace XNY.Scada.Mes;

public static class MesServiceCollectionExtensions
{
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
