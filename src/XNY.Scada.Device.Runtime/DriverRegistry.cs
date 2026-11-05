using System.Reflection;

namespace XNY.Scada.Device.Runtime;

public sealed class DriverRegistry : IDriverRegistry
{
    private readonly Dictionary<string, DriverDescriptor> _drivers = new(StringComparer.OrdinalIgnoreCase);

    public DriverRegistry(IEnumerable<Assembly> assemblies)
    {
        foreach (var type in assemblies.Distinct().SelectMany(PluginLoader.SafeGetTypes))
        {
            if (type.IsAbstract || !typeof(IPlcDriver).IsAssignableFrom(type)) continue;
            var attr = type.GetCustomAttribute<PlcDriverAttribute>();
            if (attr == null) continue;
            var options = type.GetCustomAttributes<DriverOptionAttribute>(inherit: true)
                .GroupBy(o => o.Key).Select(g => g.First())
                .OrderBy(o => o.Order)
                .Select(o => new DriverOptionSchema(o.Key, o.DisplayName, o.Type, o.Default,
                    o.Choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), o.Order))
                .ToList();
            _drivers[attr.Key] = new DriverDescriptor(attr.Key, attr.DisplayName, attr.Vendor, attr.AddressHelp, type, options);
        }
    }

    public IReadOnlyList<DriverDescriptor> Drivers => _drivers.Values.OrderBy(d => d.Vendor).ThenBy(d => d.Key).ToList();

    public DriverDescriptor? Find(string key) => _drivers.GetValueOrDefault(key);

    public IPlcDriver Create(string key, IReadOnlyDictionary<string, string> options)
    {
        var d = Find(key) ?? throw new InvalidOperationException($"未找到驱动 {key}，请确认插件已放入 plugins 目录");
        var driver = (IPlcDriver)Activator.CreateInstance(d.DriverType)!;
        var merged = d.Options.ToDictionary(o => o.Key, o => o.Default, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in options) merged[kv.Key] = kv.Value;
        driver.Configure(merged);
        return driver;
    }
}
