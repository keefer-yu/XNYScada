using System.Collections.Concurrent;

namespace XNY.Scada.Flow.Runtime;

/// <summary>全局与工位作用域变量存储（进程内共享）</summary>
public sealed class FlowVariableStore
{
    /// <summary>工位。</summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object?>> _stations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>成员发生变化时触发。</summary>
    public event EventHandler<(string Scope, string Name, object? Value)>? Changed;

    /// <summary>全局。</summary>
    public ConcurrentDictionary<string, object?> Global { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>工位。</summary>
    public ConcurrentDictionary<string, object?> Station(string? station) =>
        _stations.GetOrAdd(station ?? "", _ => new ConcurrentDictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

    /// <summary>全部工位。</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> AllStations =>
        _stations.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, object?>)kv.Value);

    /// <summary>设置全局。</summary>
    public void SetGlobal(string name, object? value)
    {
        Global[name] = value;
        Changed?.Invoke(this, ("Global", name, value));
    }

    /// <summary>设置工位。</summary>
    public void SetStation(string? station, string name, object? value)
    {
        Station(station)[name] = value;
        Changed?.Invoke(this, ($"Station:{station}", name, value));
    }
}
