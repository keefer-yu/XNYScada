using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using XNY.Scada.Device;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Expressions;
using XNY.Scada.Flow.Model;
using XNY.Scada.Mes;

namespace XNY.Scada.Flow.Runtime;

/// <summary>流运行时信息。</summary>
/// <param name="Code">编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Version">版本号。</param>
/// <param name="PendingVersion">等待空闲后切换的版本。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="RunningCount">正在运行的实例数。</param>
/// <param name="CompletedCount">累计完成次数。</param>
/// <param name="FailedCount">连续失败次数。</param>
/// <param name="LastRunAt">最近一次运行时间。</param>
/// <param name="LastError">最近一次错误。</param>
public sealed record FlowRuntimeInfo(string Code, string Name, int Version, int? PendingVersion, bool Enabled, int RunningCount, long CompletedCount, long FailedCount, DateTime? LastRunAt, string? LastError);

/// <summary>已发布流程的运行时：加载、触发、热切换，并广播节点与实例事件。</summary>
public interface IFlowEngine
{
    /// <summary>流程实例开始发生变化时触发。</summary>
    event EventHandler<FlowInstance>? InstanceStarted;

    /// <summary>流程实例结束发生变化时触发。</summary>
    event EventHandler<FlowInstance>? InstanceCompleted;

    /// <summary>节点开始执行发生变化时触发。</summary>
    event EventHandler<FlowNodeEventArgs>? NodeStarted;

    /// <summary>节点执行结束发生变化时触发。</summary>
    event EventHandler<FlowNodeEventArgs>? NodeCompleted;

    /// <summary>是否正在运行。</summary>
    bool IsRunning { get; }

    /// <summary>流程调试器。</summary>
    FlowDebugger Debugger { get; }

    /// <summary>启动。</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止。</summary>
    Task StopAsync();

    /// <summary>重新加载某个流程的发布版本；若流程正在运行，则在空闲后切换</summary>
    Task ReloadAsync(string flowCode);

    /// <summary>重新加载全部。</summary>
    Task ReloadAllAsync();

    /// <summary>获取流程。</summary>
    IReadOnlyList<FlowRuntimeInfo> GetFlows();

    /// <summary>获取运行实例。</summary>
    IReadOnlyList<FlowInstance> GetRunningInstances();

    /// <summary>触发。</summary>
    Task<FlowInstance?> TriggerAsync(string flowCode, IReadOnlyDictionary<string, object?>? input = null, string source = "Manual", CancellationToken ct = default);

    /// <summary>模拟运行草稿流程（不注册触发器，不写实例日志）</summary>
    Task<FlowInstance> RunDocumentAsync(FlowDocument document, IReadOnlyDictionary<string, object?>? input = null, CancellationToken ct = default);

    /// <summary>取消。</summary>
    void Cancel(string instanceId);
}

/// <summary>流程引擎。</summary>
public sealed class FlowEngine : IFlowEngine, IFlowRunner, IAsyncDisposable
{
    /// <summary>流运行时。</summary>
    /// <param name="flow">流。</param>
    private sealed class FlowRuntime(PublishedFlow flow)
    {
        /// <summary>流。</summary>
        public PublishedFlow Flow = flow;
        /// <summary>待处理。</summary>
        public PublishedFlow? Pending;
        /// <summary>队列。</summary>
        public readonly SemaphoreSlim Queue = new(1, 1);
        /// <summary>运行。</summary>
        public int Running;
        /// <summary>完成。</summary>
        public long Completed;
        /// <summary>失败。</summary>
        public long Failed;
        /// <summary>最近一次运行时间。</summary>
        public DateTime? LastRunAt;
        /// <summary>最近一次错误。</summary>
        public string? LastError;
        /// <summary>定时器。</summary>
        public readonly List<Timer> Timers = [];
    }

    /// <summary>存储。</summary>
    private readonly IFlowDefinitionStore _store;
    /// <summary>执行器。</summary>
    private readonly FlowExecutor _executor;
    /// <summary>点位。</summary>
    private readonly ITagService _tags;
    /// <summary>日志接收器。</summary>
    private readonly IFlowInstanceLogSink? _logSink;
    /// <summary>MES回调。</summary>
    private readonly MesCallbackListener? _mesCallback;
    /// <summary>日志。</summary>
    private readonly ILogger<FlowEngine> _logger;
    /// <summary>流程。</summary>
    private readonly ConcurrentDictionary<string, FlowRuntime> _flows = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>运行。</summary>
    private readonly ConcurrentDictionary<string, (FlowInstance Instance, CancellationTokenSource Cts)> _running = new();
    /// <summary>引擎取消源。</summary>
    private CancellationTokenSource _engineCts = new();

    /// <summary>创建流程引擎。</summary>
    public FlowEngine(
        IFlowDefinitionStore store,
        FlowExecutor executor,
        ITagService tags,
        FlowDebugger debugger,
        ILogger<FlowEngine> logger,
        IFlowInstanceLogSink? logSink = null,
        MesCallbackListener? mesCallback = null)
    {
        _store = store;
        _executor = executor;
        _tags = tags;
        _logSink = logSink;
        _mesCallback = mesCallback;
        _logger = logger;
        Debugger = debugger;
        _executor.NodeStarted += (s, e) => NodeStarted?.Invoke(this, e);
        _executor.NodeCompleted += (s, e) => NodeCompleted?.Invoke(this, e);
    }

    /// <summary>流程实例开始发生变化时触发。</summary>
    public event EventHandler<FlowInstance>? InstanceStarted;
    /// <summary>流程实例结束发生变化时触发。</summary>
    public event EventHandler<FlowInstance>? InstanceCompleted;
    /// <summary>节点开始执行发生变化时触发。</summary>
    public event EventHandler<FlowNodeEventArgs>? NodeStarted;
    /// <summary>节点执行结束发生变化时触发。</summary>
    public event EventHandler<FlowNodeEventArgs>? NodeCompleted;

    /// <summary>是否正在运行。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>流程调试器。</summary>
    public FlowDebugger Debugger { get; }

    /// <summary>启动。</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;
        _engineCts = new CancellationTokenSource();
        await LoadAllAsync();
        _tags.TagChanged += OnTagChanged;
        if (_mesCallback != null) _mesCallback.CallbackReceived += OnMesCallback;
        IsRunning = true;
        _logger.LogInformation("流程引擎已启动，加载流程 {Count} 个", _flows.Count);

        foreach (var rt in _flows.Values)
        {
            if (rt.Flow.Enabled && rt.Flow.Document.Triggers.Any(t => t.Enabled && t.Type == TriggerType.Startup))
                Fire(rt, "Startup", null, null);
        }
    }

    /// <summary>停止。</summary>
    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _tags.TagChanged -= OnTagChanged;
        if (_mesCallback != null) _mesCallback.CallbackReceived -= OnMesCallback;
        foreach (var rt in _flows.Values) DisposeTimers(rt);
        await _engineCts.CancelAsync();
        Debugger.ContinueAll();
        var deadline = DateTime.Now.AddSeconds(5);
        while (!_running.IsEmpty && DateTime.Now < deadline) await Task.Delay(50);
        _logger.LogInformation("流程引擎已停止");
    }

    /// <summary>加载全部。</summary>
    private async Task LoadAllAsync()
    {
        foreach (var rt in _flows.Values) DisposeTimers(rt);
        _flows.Clear();
        foreach (var f in await _store.GetPublishedAsync())
        {
            var rt = new FlowRuntime(f);
            _flows[f.Code] = rt;
            StartTimers(rt);
        }
    }

    /// <summary>重新加载全部。</summary>
    public async Task ReloadAllAsync()
    {
        var published = await _store.GetPublishedAsync();
        foreach (var f in published) await ApplyAsync(f);
        foreach (var code in _flows.Keys.Except(published.Select(p => p.Code), StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (_flows.TryRemove(code, out var rt)) DisposeTimers(rt);
        }
    }

    /// <summary>重新加载。</summary>
    public async Task ReloadAsync(string flowCode)
    {
        var f = await _store.GetPublishedAsync(flowCode);
        if (f == null)
        {
            if (_flows.TryRemove(flowCode, out var removed)) DisposeTimers(removed);
            return;
        }
        await ApplyAsync(f);
    }

    /// <summary>应用。</summary>
    private Task ApplyAsync(PublishedFlow f)
    {
        if (!_flows.TryGetValue(f.Code, out var rt))
        {
            rt = new FlowRuntime(f);
            _flows[f.Code] = rt;
            if (IsRunning) StartTimers(rt);
            return Task.CompletedTask;
        }
        lock (rt)
        {
            if (rt.Running == 0)
            {
                SwitchTo(rt, f);
            }
            else
            {
                rt.Pending = f;
                _logger.LogInformation("流程 {Flow} 运行中，v{Version} 将在空闲后生效", f.Code, f.Version);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>切换到。</summary>
    private void SwitchTo(FlowRuntime rt, PublishedFlow f)
    {
        DisposeTimers(rt);
        rt.Flow = f;
        rt.Pending = null;
        if (IsRunning) StartTimers(rt);
        _logger.LogInformation("流程 {Flow} 已切换到 v{Version}", f.Code, f.Version);
    }

    /// <summary>启动定时器。</summary>
    private void StartTimers(FlowRuntime rt)
    {
        foreach (var t in rt.Flow.Document.Triggers.Where(t => t.Enabled && t.Type == TriggerType.Timer && t.IntervalMs > 0))
        {
            var trigger = t;
            rt.Timers.Add(new Timer(_ =>
            {
                if (IsRunning && ConditionOk(trigger)) Fire(rt, trigger.ToString(), trigger.Station, null);
            }, null, trigger.IntervalMs, trigger.IntervalMs));
        }
    }

    /// <summary>释放资源定时器。</summary>
    private static void DisposeTimers(FlowRuntime rt)
    {
        foreach (var t in rt.Timers) t.Dispose();
        rt.Timers.Clear();
    }

    /// <summary>当标记变化时更新关联状态。</summary>
    private void OnTagChanged(object? sender, TagChangedEventArgs e)
    {
        if (e.NewValue.Quality != TagQuality.Good) return;
        foreach (var rt in _flows.Values)
        {
            foreach (var t in rt.Flow.Document.Triggers)
            {
                if (!t.Enabled || !string.Equals(t.Tag, e.Tag.FullName, StringComparison.OrdinalIgnoreCase)) continue;
                var hit = t.Type switch
                {
                    TriggerType.TagRisingEdge => e.IsRisingEdge,
                    TriggerType.TagFallingEdge => e.IsFallingEdge,
                    TriggerType.TagChanged => e.OldValue != null,
                    _ => false
                };
                if (hit && ConditionOk(t))
                {
                    Fire(rt, t.ToString(), t.Station, new Dictionary<string, object?> { ["TriggerValue"] = e.NewValue.Value });
                }
            }
        }
    }

    /// <summary>处理MES回调。</summary>
    private void OnMesCallback(object? sender, MesCallbackEventArgs e)
    {
        foreach (var rt in _flows.Values)
        {
            foreach (var t in rt.Flow.Document.Triggers.Where(t => t.Enabled && t.Type == TriggerType.MesCallback))
            {
                if (string.Equals(t.Source, e.Path, StringComparison.OrdinalIgnoreCase) && ConditionOk(t, e.Data))
                    Fire(rt, t.ToString(), t.Station, e.Data);
            }
        }
    }

    /// <summary>条件OK。</summary>
    private bool ConditionOk(TriggerDef t, IReadOnlyDictionary<string, object?>? vars = null)
    {
        if (string.IsNullOrWhiteSpace(t.Condition)) return true;
        try
        {
            return ScadaExpression.EvaluateBool(t.Condition, vars, (kind, name) => kind.Equals("Tag", StringComparison.OrdinalIgnoreCase) ? _tags.GetValue(name)?.Value : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("触发条件 {Condition} 计算失败: {Message}", t.Condition, ex.Message);
            return false;
        }
    }

    /// <summary>触发。</summary>
    private void Fire(FlowRuntime rt, string source, string? station, IReadOnlyDictionary<string, object?>? input)
    {
        if (!rt.Flow.Enabled) return;
        _ = RunAsync(rt, source, station, input, _engineCts.Token);
    }

    /// <summary>触发。</summary>
    public async Task<FlowInstance?> TriggerAsync(string flowCode, IReadOnlyDictionary<string, object?>? input = null, string source = "Manual", CancellationToken ct = default)
    {
        if (!_flows.TryGetValue(flowCode, out var rt)) throw new FlowException($"流程未发布或不存在: {flowCode}");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _engineCts.Token);
        return await RunAsync(rt, source, null, input, linked.Token);
    }

    /// <summary>运行。</summary>
    private async Task<FlowInstance?> RunAsync(FlowRuntime rt, string source, string? station, IReadOnlyDictionary<string, object?>? input, CancellationToken ct)
    {
        var doc = rt.Flow.Document;
        switch (doc.Concurrency)
        {
            case FlowConcurrency.Skip:
                if (!rt.Queue.Wait(0))
                {
                    _logger.LogDebug("流程 {Flow} 运行中，忽略触发 {Source}", doc.Code, source);
                    return null;
                }
                break;
            case FlowConcurrency.Queue:
                await rt.Queue.WaitAsync(ct);
                break;
        }

        lock (rt) rt.Running++;
        var instance = new FlowInstance(rt.Flow.Document, source, station, input, false);
        try
        {
            await ExecuteAsync(instance, ct);
            rt.LastRunAt = instance.StartedAt;
            if (instance.Status == FlowInstanceStatus.Completed) Interlocked.Increment(ref rt.Completed);
            else
            {
                Interlocked.Increment(ref rt.Failed);
                rt.LastError = instance.Error;
            }
            return instance;
        }
        finally
        {
            lock (rt)
            {
                rt.Running--;
                if (rt.Running == 0 && rt.Pending != null) SwitchTo(rt, rt.Pending);
            }
            if (doc.Concurrency != FlowConcurrency.Parallel) rt.Queue.Release();
            OnInstanceFinished(instance);
        }
    }

    /// <summary>执行。</summary>
    private async Task ExecuteAsync(FlowInstance instance, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _running[instance.Id] = (instance, cts);
        InstanceStarted?.Invoke(this, instance);
        try
        {
            await Task.Run(() => _executor.RunAsync(instance, this, cts.Token), CancellationToken.None);
        }
        finally
        {
            _running.TryRemove(instance.Id, out _);
        }
        if (_logSink != null && !instance.IsSimulation)
        {
            try
            {
                await _logSink.WriteAsync(instance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写流程实例日志失败");
            }
        }
        InstanceCompleted?.Invoke(this, instance);
    }

    /// <summary>处理实例已完成。</summary>
    private void OnInstanceFinished(FlowInstance instance)
    {
        if (!IsRunning || instance.IsSimulation) return;
        foreach (var rt in _flows.Values)
        {
            foreach (var t in rt.Flow.Document.Triggers.Where(t => t.Enabled && t.Type == TriggerType.FlowCompleted))
            {
                if (!string.Equals(t.Source, instance.FlowCode, StringComparison.OrdinalIgnoreCase)) continue;
                var vars = new Dictionary<string, object?>(instance.Variables, StringComparer.OrdinalIgnoreCase)
                {
                    ["SourceStatus"] = instance.Status.ToString()
                };
                if (ConditionOk(t, vars)) Fire(rt, t.ToString(), t.Station ?? instance.Station, vars);
            }
        }
    }

    /// <summary>运行文档。</summary>
    public async Task<FlowInstance> RunDocumentAsync(FlowDocument document, IReadOnlyDictionary<string, object?>? input = null, CancellationToken ct = default)
    {
        var instance = new FlowInstance(document, "Simulation", document.Station, input, true);
        await ExecuteAsync(instance, ct);
        return instance;
    }

    /// <summary>运行子级流。</summary>
    public async Task<FlowInstance> RunSubFlowAsync(string flowCode, IReadOnlyDictionary<string, object?> input, FlowInstance parent, CancellationToken ct)
    {
        FlowDocument doc;
        if (_flows.TryGetValue(flowCode, out var rt))
        {
            doc = rt.Flow.Document;
        }
        else
        {
            var f = await _store.GetPublishedAsync(flowCode) ?? throw new FlowException($"子流程未发布: {flowCode}");
            doc = f.Document;
        }
        var instance = new FlowInstance(doc, $"SubFlow:{parent.FlowCode}", parent.Station, input, parent.IsSimulation);
        await _executor.RunAsync(instance, this, ct);
        return instance;
    }

    /// <summary>获取流程。</summary>
    public IReadOnlyList<FlowRuntimeInfo> GetFlows() => _flows.Values
        .Select(rt => new FlowRuntimeInfo(rt.Flow.Code, rt.Flow.Name, rt.Flow.Version, rt.Pending?.Version, rt.Flow.Enabled,
            rt.Running, Interlocked.Read(ref rt.Completed), Interlocked.Read(ref rt.Failed), rt.LastRunAt, rt.LastError))
        .OrderBy(f => f.Code)
        .ToList();

    /// <summary>获取运行实例。</summary>
    public IReadOnlyList<FlowInstance> GetRunningInstances() => _running.Values.Select(v => v.Instance).ToList();

    /// <summary>取消。</summary>
    public void Cancel(string instanceId)
    {
        if (_running.TryGetValue(instanceId, out var r))
        {
            r.Cts.Cancel();
            Debugger.Continue(instanceId);
        }
    }

    /// <summary>释放资源。</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _engineCts.Dispose();
    }
}
