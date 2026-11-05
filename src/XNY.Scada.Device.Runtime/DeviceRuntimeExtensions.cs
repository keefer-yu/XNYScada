using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using XNY.Scada.Device.Hsl;

namespace XNY.Scada.Device.Runtime;

public static class DeviceRuntimeExtensions
{
    /// <summary>内置驱动程序集 + 插件目录中的程序集</summary>
    public static IReadOnlyList<Assembly> GetDriverAssemblies(string pluginDirectory) =>
        new[] { typeof(SimulatorDriver).Assembly, typeof(HslDriverBase).Assembly }
            .Concat(PluginLoader.Load(pluginDirectory))
            .Distinct()
            .ToList();

    public static IServiceCollection AddDeviceRuntime(this IServiceCollection services, string pluginDirectory = "plugins")
    {
        services.AddSingleton<IDriverRegistry>(_ => new DriverRegistry(GetDriverAssemblies(pluginDirectory)));
        services.AddSingleton<DeviceManager>();
        services.AddSingleton<IDeviceManager>(sp => sp.GetRequiredService<DeviceManager>());
        services.AddSingleton<ITagService>(sp => sp.GetRequiredService<DeviceManager>());
        services.AddSingleton<AlarmService>();
        services.AddSingleton<Domain.Services.IAlarmService>(sp => sp.GetRequiredService<AlarmService>());
        return services;
    }
}
