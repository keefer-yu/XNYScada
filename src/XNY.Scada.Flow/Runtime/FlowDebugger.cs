using System.Collections.Concurrent;

namespace XNY.Scada.Flow.Runtime;

/// <summary>流已暂停事件参数。</summary>
/// <param name="instance">实例。</param>
/// <param name="nodeId">节点标识。</param>
public sealed class FlowPausedEventArgs(FlowInstance instance, string nodeId) : EventArgs
{
    /// <summary>实例。</summary>
    public FlowInstance Instance { get; } = instance;
    /// <summary>节点标识。</summary>
    public string NodeId { get; } = nodeId;
}

/// <summary>断点与单步调试。仅在 <see cref="Enabled"/> 时生效，避免影响正常生产。</summary>
public sealed class FlowDebugger
{
    /// <summary>断点。</summary>
    private readonly ConcurrentDictionary<string, byte> _breakpoints = new();
    /// <summary>已暂停。</summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _paused = new();

    /// <summary>已暂停发生变化时触发。</summary>
    public event EventHandler<FlowPausedEventArgs>? Paused;

    /// <summary>已恢复发生变化时触发。</summary>
    public event EventHandler<string>? Resumed;

    /// <summary>是否流调试器是否启用。</summary>
    public bool Enabled { get; set; }

    /// <summary>单步模式：每个节点前都暂停</summary>
    public bool StepMode { get; set; }

    /// <summary>断点。</summary>
    public IReadOnlyCollection<string> Breakpoints => _breakpoints.Keys.ToList();

    /// <summary>已暂停实例。</summary>
    public IReadOnlyCollection<string> PausedInstances => _paused.Keys.ToList();

    /// <summary>键。</summary>
    private static string Key(string flowCode, string nodeId) => $"{flowCode}/{nodeId}";

    /// <summary>切换断点。</summary>
    public void ToggleBreakpoint(string flowCode, string nodeId)
    {
        var key = Key(flowCode, nodeId);
        if (!_breakpoints.TryRemove(key, out _)) _breakpoints[key] = 0;
    }

    /// <summary>是否有断点。</summary>
    public bool HasBreakpoint(string flowCode, string nodeId) => _breakpoints.ContainsKey(Key(flowCode, nodeId));

    /// <summary>清空断点。</summary>
    public void ClearBreakpoints() => _breakpoints.Clear();

    /// <summary>之前节点。</summary>
    internal async Task BeforeNodeAsync(FlowInstance instance, string nodeId, CancellationToken ct)
    {
        if (!Enabled) return;
        if (!StepMode && !HasBreakpoint(instance.FlowCode, nodeId)) return;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _paused[instance.Id] = tcs;
        Paused?.Invoke(this, new FlowPausedEventArgs(instance, nodeId));
        await using var reg = ct.Register(() => tcs.TrySetCanceled());
        await tcs.Task;
    }

    /// <summary>继续执行（单步模式下即执行下一个节点后再暂停）</summary>
    public void Continue(string instanceId)
    {
        if (_paused.TryRemove(instanceId, out var tcs))
        {
            tcs.TrySetResult(true);
            Resumed?.Invoke(this, instanceId);
        }
    }

    /// <summary>继续全部。</summary>
    public void ContinueAll()
    {
        foreach (var id in _paused.Keys.ToList()) Continue(id);
    }
}
