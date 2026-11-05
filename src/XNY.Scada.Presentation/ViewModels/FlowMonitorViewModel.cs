using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Flows;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>流运行时项。</summary>
public sealed partial class FlowRuntimeItem : ObservableObject
{
    /// <summary>流运行时编码。</summary>
    public string Code { get; init; } = "";

    /// <summary>流运行时名称。</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>版本。</summary>
    [ObservableProperty] private int _version;
    /// <summary>待处理版本。</summary>
    [ObservableProperty] private int? _pendingVersion;
    /// <summary>启用。</summary>
    [ObservableProperty] private bool _enabled;
    /// <summary>运行数量。</summary>
    [ObservableProperty] private int _runningCount;
    /// <summary>完成数量。</summary>
    [ObservableProperty] private long _completedCount;
    /// <summary>失败数量。</summary>
    [ObservableProperty] private long _failedCount;
    /// <summary>最近文本段于。</summary>
    [ObservableProperty] private DateTime? _lastRunAt;
    /// <summary>最近错误。</summary>
    [ObservableProperty] private string? _lastError;

    /// <summary>版本文本。</summary>
    public string VersionText => PendingVersion is { } p ? $"v{Version} → v{p}（待切换）" : $"v{Version}";

    /// <summary>更新。</summary>
    public void Update(FlowRuntimeInfo i)
    {
        Name = i.Name;
        Version = i.Version;
        PendingVersion = i.PendingVersion;
        Enabled = i.Enabled;
        RunningCount = i.RunningCount;
        CompletedCount = i.CompletedCount;
        FailedCount = i.FailedCount;
        LastRunAt = i.LastRunAt;
        LastError = i.LastError;
        OnPropertyChanged(nameof(VersionText));
    }
}

/// <summary>运行实例项。</summary>
/// <param name="Id">标识。</param>
/// <param name="FlowCode">流代码。</param>
/// <param name="Version">版本号。</param>
/// <param name="TriggerSource">触发器源。</param>
/// <param name="StartedAt">已启动于。</param>
/// <param name="CurrentNode">当前节点。</param>
/// <param name="Paused">已暂停。</param>
public sealed record RunningInstanceItem(string Id, string FlowCode, int Version, string TriggerSource, DateTime StartedAt, string? CurrentNode, bool Paused);

/// <summary>流事件行。</summary>
/// <param name="Time">时间。</param>
/// <param name="FlowCode">流代码。</param>
/// <param name="InstanceId">实例标识。</param>
/// <param name="Text">文本。</param>
/// <param name="IsError">是否错误。</param>
public sealed record FlowEventLine(DateTime Time, string FlowCode, string InstanceId, string Text, bool IsError);

/// <summary>流程监控页面的视图模型。</summary>
public sealed partial class FlowMonitorViewModel : ViewModelBase
{
    /// <summary>最大事件行。</summary>
    private const int MaxEventLines = 500;
    /// <summary>引擎。</summary>
    private readonly IFlowEngine _engine;
    /// <summary>日志。</summary>
    private readonly FlowLogQueryService _logs;
    /// <summary>定义。</summary>
    private readonly FlowDefinitionService _definitions;

    /// <summary>创建流程监控视图模型。</summary>
    public FlowMonitorViewModel(ViewModelContext context, IFlowEngine engine, FlowLogQueryService logs, FlowDefinitionService definitions) : base(context)
    {
        _engine = engine;
        _logs = logs;
        _definitions = definitions;

        _engine.NodeCompleted += (_, e) =>
        {
            if (!IsActive || !LiveEvents) return;
            var t = e.Trace;
            var text = $"{e.Node.DisplayName} → {t?.Port} ({t?.DurationMs}ms){(t?.Message is { } m ? " " + m : "")}{(t?.Error is { } err ? " 错误:" + err : "")}";
            AddEvent(new FlowEventLine(DateTime.Now, e.Instance.FlowCode, e.Instance.Id, text, t?.Error != null));
        };
        _engine.InstanceCompleted += (_, i) =>
        {
            if (!IsActive || !LiveEvents) return;
            AddEvent(new FlowEventLine(DateTime.Now, i.FlowCode, i.Id, $"实例结束：{i.Status} 耗时 {i.DurationMs}ms {i.Error}", i.Status != FlowInstanceStatus.Completed));
        };
        _engine.Debugger.Paused += (_, e) => Ui.BeginInvoke(() =>
        {
            AddEvent(new FlowEventLine(DateTime.Now, e.Instance.FlowCode, e.Instance.Id, $"⏸ 暂停于节点 {e.NodeId}", false));
            RefreshInstances();
        });

        TriggerCommand = Secured<FlowRuntimeItem>(Permissions.Production.ManualTrigger, TriggerAsync);
        ToggleEnabledCommand = Secured<FlowRuntimeItem>(Permissions.Flow.Control, async f =>
        {
            if (f == null) return;
            await _definitions.SetEnabledAsync(f.Code, !f.Enabled);
        });
        CancelInstanceCommand = Secured<RunningInstanceItem>(Permissions.Flow.Control, i =>
        {
            if (i != null && Dialogs.Confirm($"取消流程实例 {i.FlowCode}/{i.Id}？")) _engine.Cancel(i.Id);
            return Task.CompletedTask;
        });
        ContinueCommand = Secured<RunningInstanceItem>(Permissions.Flow.Debug, i =>
        {
            if (i != null) _engine.Debugger.Continue(i.Id);
            return Task.CompletedTask;
        });
        ContinueAllCommand = Secured(Permissions.Flow.Debug, () =>
        {
            _engine.Debugger.ContinueAll();
            return Task.CompletedTask;
        });
        ClearBreakpointsCommand = Secured(Permissions.Flow.Debug, () =>
        {
            _engine.Debugger.ClearBreakpoints();
            return Task.CompletedTask;
        });
        ReloadAllCommand = Secured(Permissions.Flow.Control, () => _engine.ReloadAllAsync());
        QueryLogsCommand = Command(QueryLogsAsync);
        ClearEventsCommand = Command(() => { Events.Clear(); return Task.CompletedTask; });
    }

    /// <summary>标题。</summary>
    public override string Title => "流程监控";

    /// <summary>流程数量。</summary>
    public ObservableCollection<FlowRuntimeItem> Flows { get; } = [];
    /// <summary>实例。</summary>
    public ObservableCollection<RunningInstanceItem> Instances { get; } = [];
    /// <summary>事件。</summary>
    public ObservableCollection<FlowEventLine> Events { get; } = [];
    /// <summary>日志。</summary>
    public ObservableCollection<FlowInstanceLogEntity> Logs { get; } = [];
    /// <summary>选中跟踪。</summary>
    public ObservableCollection<NodeTrace> SelectedTrace { get; } = [];

    /// <summary>状态筛选条件。</summary>
    public IReadOnlyList<string> StatusFilters { get; } = ["全部", .. Enum.GetNames<FlowInstanceStatus>()];

    /// <summary>选中流。</summary>
    [ObservableProperty] private FlowRuntimeItem? _selectedFlow;
    /// <summary>选中日志。</summary>
    [ObservableProperty] private FlowInstanceLogEntity? _selectedLog;
    /// <summary>实时事件。</summary>
    [ObservableProperty] private bool _liveEvents = true;
    /// <summary>引擎运行。</summary>
    [ObservableProperty] private bool _engineRunning;
    /// <summary>日志从。</summary>
    [ObservableProperty] private DateTime _logFrom = DateTime.Today;
    /// <summary>日志到。</summary>
    [ObservableProperty] private DateTime _logTo = DateTime.Today.AddDays(1);
    /// <summary>日志状态。</summary>
    [ObservableProperty] private string _logStatus = "全部";
    /// <summary>触发器输入。</summary>
    [ObservableProperty] private string? _triggerInput;

    /// <summary>是否调试启用。</summary>
    public bool DebugEnabled
    {
        get => _engine.Debugger.Enabled;
        set
        {
            if (value && !Can(Permissions.Flow.Debug)) return;
            _engine.Debugger.Enabled = value;
            if (!value) _engine.Debugger.ContinueAll();
            OnPropertyChanged();
        }
    }

    /// <summary>是否步进模式。</summary>
    public bool StepMode
    {
        get => _engine.Debugger.StepMode;
        set
        {
            if (value && !Can(Permissions.Flow.Debug)) return;
            _engine.Debugger.StepMode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>断点文本。</summary>
    public string BreakpointsText => string.Join(", ", _engine.Debugger.Breakpoints);

    /// <summary>触发器命令。</summary>
    public IAsyncRelayCommand<FlowRuntimeItem> TriggerCommand { get; }
    /// <summary>切换启用命令。</summary>
    public IAsyncRelayCommand<FlowRuntimeItem> ToggleEnabledCommand { get; }
    /// <summary>是否取消实例命令。</summary>
    public IAsyncRelayCommand<RunningInstanceItem> CancelInstanceCommand { get; }
    /// <summary>继续命令。</summary>
    public IAsyncRelayCommand<RunningInstanceItem> ContinueCommand { get; }
    /// <summary>继续全部命令。</summary>
    public IAsyncRelayCommand ContinueAllCommand { get; }
    /// <summary>清除断点命令。</summary>
    public IAsyncRelayCommand ClearBreakpointsCommand { get; }
    /// <summary>重载全部命令。</summary>
    public IAsyncRelayCommand ReloadAllCommand { get; }
    /// <summary>查询日志命令。</summary>
    public IAsyncRelayCommand QueryLogsCommand { get; }
    /// <summary>清除事件命令。</summary>
    public IAsyncRelayCommand ClearEventsCommand { get; }

    /// <summary>处理激活。</summary>
    protected override async Task OnActivatedAsync()
    {
        Refresh();
        await QueryLogsAsync();
        StartRefresh(TimeSpan.FromSeconds(1), () =>
        {
            Refresh();
            return Task.CompletedTask;
        });
    }

    /// <summary>添加事件。</summary>
    private void AddEvent(FlowEventLine line) => Ui.BeginInvoke(() =>
    {
        Events.Insert(0, line);
        while (Events.Count > MaxEventLines) Events.RemoveAt(Events.Count - 1);
    });

    /// <summary>刷新。</summary>
    private void Refresh()
    {
        EngineRunning = _engine.IsRunning;
        var flows = _engine.GetFlows();
        foreach (var f in flows)
        {
            var item = Flows.FirstOrDefault(x => x.Code == f.Code);
            if (item == null) Flows.Add(item = new FlowRuntimeItem { Code = f.Code });
            item.Update(f);
        }
        foreach (var gone in Flows.Where(x => flows.All(f => f.Code != x.Code)).ToList()) Flows.Remove(gone);
        RefreshInstances();
        OnPropertyChanged(nameof(BreakpointsText));
        OnPropertyChanged(nameof(DebugEnabled));
        OnPropertyChanged(nameof(StepMode));
    }

    /// <summary>刷新实例。</summary>
    private void RefreshInstances()
    {
        var paused = _engine.Debugger.PausedInstances.ToHashSet();
        var list = _engine.GetRunningInstances()
            .Select(i => new RunningInstanceItem(i.Id, i.FlowCode, i.Document.Version, i.TriggerSource, i.StartedAt,
                i.CurrentNodeId == null ? null : i.Document.FindNode(i.CurrentNodeId)?.DisplayName ?? i.CurrentNodeId, paused.Contains(i.Id)))
            .ToList();
        if (!list.SequenceEqual(Instances)) Replace(Instances, list);
    }

    /// <summary>触发。</summary>
    private async Task TriggerAsync(FlowRuntimeItem? flow)
    {
        if (flow == null) return;
        var input = string.IsNullOrWhiteSpace(TriggerInput) ? null : Json.ToDictionary(TriggerInput);
        var instance = await _engine.TriggerAsync(flow.Code, input, $"Manual:{Context.User.UserName}");
        StatusMessage = instance == null ? $"{flow.Code} 正在运行，本次触发被忽略" : $"{flow.Code} 结束：{instance.Status} {instance.Error}";
        await QueryLogsAsync();
    }

    /// <summary>查询日志。</summary>
    private async Task QueryLogsAsync()
    {
        FlowInstanceStatus? status = Enum.TryParse<FlowInstanceStatus>(LogStatus, out var s) ? s : null;
        Replace(Logs, await _logs.QueryAsync(SelectedFlow?.Code, LogFrom, LogTo, status));
    }

    /// <summary>当选中流变化时更新关联状态。</summary>
    partial void OnSelectedFlowChanged(FlowRuntimeItem? value)
    {
        if (IsActive) _ = RunAsync(QueryLogsAsync);
    }

    /// <summary>当选中日志变化时更新关联状态。</summary>
    partial void OnSelectedLogChanged(FlowInstanceLogEntity? value) =>
        Replace(SelectedTrace, Json.Deserialize<List<NodeTrace>>(value?.TraceJson) ?? []);
}
