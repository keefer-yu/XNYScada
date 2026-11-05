using System.Collections.Concurrent;

namespace XNY.Scada.Flow.Runtime;

public sealed class FlowPausedEventArgs(FlowInstance instance, string nodeId) : EventArgs
{
    public FlowInstance Instance { get; } = instance;
    public string NodeId { get; } = nodeId;
}

/// <summary>断点与单步调试。仅在 <see cref="Enabled"/> 时生效，避免影响正常生产。</summary>
public sealed class FlowDebugger
{
    private readonly ConcurrentDictionary<string, byte> _breakpoints = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _paused = new();

    public event EventHandler<FlowPausedEventArgs>? Paused;

    public event EventHandler<string>? Resumed;

    public bool Enabled { get; set; }

    /// <summary>单步模式：每个节点前都暂停</summary>
    public bool StepMode { get; set; }

    public IReadOnlyCollection<string> Breakpoints => _breakpoints.Keys.ToList();

    public IReadOnlyCollection<string> PausedInstances => _paused.Keys.ToList();

    private static string Key(string flowCode, string nodeId) => $"{flowCode}/{nodeId}";

    public void ToggleBreakpoint(string flowCode, string nodeId)
    {
        var key = Key(flowCode, nodeId);
        if (!_breakpoints.TryRemove(key, out _)) _breakpoints[key] = 0;
    }

    public bool HasBreakpoint(string flowCode, string nodeId) => _breakpoints.ContainsKey(Key(flowCode, nodeId));

    public void ClearBreakpoints() => _breakpoints.Clear();

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

    public void ContinueAll()
    {
        foreach (var id in _paused.Keys.ToList()) Continue(id);
    }
}
