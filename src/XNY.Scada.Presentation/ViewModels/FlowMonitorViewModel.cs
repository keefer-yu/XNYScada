using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Flows;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class FlowRuntimeItem : ObservableObject
{
    public string Code { get; init; } = "";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _version;
    [ObservableProperty] private int? _pendingVersion;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private long _completedCount;
    [ObservableProperty] private long _failedCount;
    [ObservableProperty] private DateTime? _lastRunAt;
    [ObservableProperty] private string? _lastError;

    public string VersionText => PendingVersion is { } p ? $"v{Version} → v{p}（待切换）" : $"v{Version}";

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

public sealed record RunningInstanceItem(string Id, string FlowCode, int Version, string TriggerSource, DateTime StartedAt, string? CurrentNode, bool Paused);

public sealed record FlowEventLine(DateTime Time, string FlowCode, string InstanceId, string Text, bool IsError);

public sealed partial class FlowMonitorViewModel : ViewModelBase
{
    private const int MaxEventLines = 500;
    private readonly IFlowEngine _engine;
    private readonly FlowLogQueryService _logs;
    private readonly FlowDefinitionService _definitions;

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

    public override string Title => "流程监控";

    public ObservableCollection<FlowRuntimeItem> Flows { get; } = [];
    public ObservableCollection<RunningInstanceItem> Instances { get; } = [];
    public ObservableCollection<FlowEventLine> Events { get; } = [];
    public ObservableCollection<FlowInstanceLogEntity> Logs { get; } = [];
    public ObservableCollection<NodeTrace> SelectedTrace { get; } = [];

    public IReadOnlyList<string> StatusFilters { get; } = ["全部", .. Enum.GetNames<FlowInstanceStatus>()];

    [ObservableProperty] private FlowRuntimeItem? _selectedFlow;
    [ObservableProperty] private FlowInstanceLogEntity? _selectedLog;
    [ObservableProperty] private bool _liveEvents = true;
    [ObservableProperty] private bool _engineRunning;
    [ObservableProperty] private DateTime _logFrom = DateTime.Today;
    [ObservableProperty] private DateTime _logTo = DateTime.Today.AddDays(1);
    [ObservableProperty] private string _logStatus = "全部";
    [ObservableProperty] private string? _triggerInput;

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

    public string BreakpointsText => string.Join(", ", _engine.Debugger.Breakpoints);

    public IAsyncRelayCommand<FlowRuntimeItem> TriggerCommand { get; }
    public IAsyncRelayCommand<FlowRuntimeItem> ToggleEnabledCommand { get; }
    public IAsyncRelayCommand<RunningInstanceItem> CancelInstanceCommand { get; }
    public IAsyncRelayCommand<RunningInstanceItem> ContinueCommand { get; }
    public IAsyncRelayCommand ContinueAllCommand { get; }
    public IAsyncRelayCommand ClearBreakpointsCommand { get; }
    public IAsyncRelayCommand ReloadAllCommand { get; }
    public IAsyncRelayCommand QueryLogsCommand { get; }
    public IAsyncRelayCommand ClearEventsCommand { get; }

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

    private void AddEvent(FlowEventLine line) => Ui.BeginInvoke(() =>
    {
        Events.Insert(0, line);
        while (Events.Count > MaxEventLines) Events.RemoveAt(Events.Count - 1);
    });

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

    private void RefreshInstances()
    {
        var paused = _engine.Debugger.PausedInstances.ToHashSet();
        var list = _engine.GetRunningInstances()
            .Select(i => new RunningInstanceItem(i.Id, i.FlowCode, i.Document.Version, i.TriggerSource, i.StartedAt,
                i.CurrentNodeId == null ? null : i.Document.FindNode(i.CurrentNodeId)?.DisplayName ?? i.CurrentNodeId, paused.Contains(i.Id)))
            .ToList();
        if (!list.SequenceEqual(Instances)) Replace(Instances, list);
    }

    private async Task TriggerAsync(FlowRuntimeItem? flow)
    {
        if (flow == null) return;
        var input = string.IsNullOrWhiteSpace(TriggerInput) ? null : Json.ToDictionary(TriggerInput);
        var instance = await _engine.TriggerAsync(flow.Code, input, $"Manual:{Context.User.UserName}");
        StatusMessage = instance == null ? $"{flow.Code} 正在运行，本次触发被忽略" : $"{flow.Code} 结束：{instance.Status} {instance.Error}";
        await QueryLogsAsync();
    }

    private async Task QueryLogsAsync()
    {
        FlowInstanceStatus? status = Enum.TryParse<FlowInstanceStatus>(LogStatus, out var s) ? s : null;
        Replace(Logs, await _logs.QueryAsync(SelectedFlow?.Code, LogFrom, LogTo, status));
    }

    partial void OnSelectedFlowChanged(FlowRuntimeItem? value)
    {
        if (IsActive) _ = RunAsync(QueryLogsAsync);
    }

    partial void OnSelectedLogChanged(FlowInstanceLogEntity? value) =>
        Replace(SelectedTrace, Json.Deserialize<List<NodeTrace>>(value?.TraceJson) ?? []);
}
