using System.Reflection;

namespace XNY.Scada.Device.Runtime;

/// <summary>驱动注册。</summary>
public sealed class DriverRegistry : IDriverRegistry
{
    /// <summary>驱动。</summary>
    private readonly Dictionary<string, DriverDescriptor> _drivers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>创建驱动注册。</summary>
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

    /// <summary>驱动。</summary>
    public IReadOnlyList<DriverDescriptor> Drivers => _drivers.Values.OrderBy(d => d.Vendor).ThenBy(d => d.Key).ToList();

    /// <summary>查找。</summary>
    public DriverDescriptor? Find(string key) => _drivers.GetValueOrDefault(key);

    /// <summary>创建。</summary>
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
