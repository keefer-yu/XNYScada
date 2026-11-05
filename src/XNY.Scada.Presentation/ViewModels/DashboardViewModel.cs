using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Production;
using XNY.Scada.Application.Settings;
using XNY.Scada.Device;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>看板页面的视图模型。</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    /// <summary>设备。</summary>
    private readonly IDeviceManager _devices;
    /// <summary>点位。</summary>
    private readonly ITagService _tags;
    /// <summary>生产。</summary>
    private readonly IProductionService _production;
    /// <summary>查询。</summary>
    private readonly ProductionQueryService _query;
    /// <summary>托盘。</summary>
    private readonly ITrayService _trays;
    /// <summary>配方。</summary>
    private readonly RecipeService _recipes;
    /// <summary>参数。</summary>
    private readonly SystemParamService _params;
    /// <summary>引擎。</summary>
    private readonly IFlowEngine _engine;
    /// <summary>报警。</summary>
    private readonly IAlarmService _alarms;
    /// <summary>审计。</summary>
    private readonly IAuditService _audit;

    /// <summary>创建看板视图模型。</summary>
    public DashboardViewModel(
        ViewModelContext context, IDeviceManager devices, ITagService tags, IProductionService production,
        ProductionQueryService query, ITrayService trays, RecipeService recipes, SystemParamService parameters,
        IFlowEngine engine, IAlarmService alarms, IAuditService audit) : base(context)
    {
        _devices = devices;
        _tags = tags;
        _production = production;
        _query = query;
        _trays = trays;
        _recipes = recipes;
        _params = parameters;
        _engine = engine;
        _alarms = alarms;
        _audit = audit;

        _production.RecordSaved += (_, r) => Ui.BeginInvoke(() =>
        {
            RecentRecords.Insert(0, r);
            while (RecentRecords.Count > 100) RecentRecords.RemoveAt(RecentRecords.Count - 1);
        });
        _trays.TrayChanged += (_, code) => Ui.BeginInvoke(async () => await LoadTrayAsync(code));

        StartProductionCommand = Secured(Permissions.Production.Start, StartProductionAsync, () => !EngineRunning);
        StopProductionCommand = Secured(Permissions.Production.Start, StopProductionAsync, () => EngineRunning);
        ResetCountersCommand = Secured(Permissions.Production.ResetCounter, ResetCountersAsync);
        ResetBinCommand = Secured<BinItem>(Permissions.Production.ResetCounter, ResetBinAsync);
        TriggerFlowCommand = Secured<string>(Permissions.Production.ManualTrigger, TriggerFlowAsync);
        AcknowledgeAllCommand = Secured(Permissions.Alarm.Acknowledge, async () =>
        {
            foreach (var a in _alarms.ActiveAlarms) await _alarms.AcknowledgeAsync(a.Id, Context.User.UserName);
        });
    }

    /// <summary>标题。</summary>
    public override string Title => "主监控";

    /// <summary>设备数量。</summary>
    public ObservableCollection<DeviceStatusItem> Devices { get; } = [];
    /// <summary>托盘单元格。</summary>
    public ObservableCollection<TrayCellItem> TrayCells { get; } = [];
    /// <summary>档位。</summary>
    public ObservableCollection<BinItem> Bins { get; } = [];
    /// <summary>NG数量。</summary>
    public ObservableCollection<NgCountItem> NgCounts { get; } = [];
    /// <summary>主界面关键点位。</summary>
    public ObservableCollection<TagValueItem> KeyTags { get; } = [];
    /// <summary>最近记录。</summary>
    public ObservableCollection<CellRecordEntity> RecentRecords { get; } = [];
    /// <summary>活动报警。</summary>
    public ObservableCollection<AlarmRecordEntity> ActiveAlarms { get; } = [];
    /// <summary>允许手动触发的流程。</summary>
    public ObservableCollection<string> ManualFlows { get; } = [];

    /// <summary>机台类型名称。</summary>
    [ObservableProperty] private string _machineTypeName = "";
    /// <summary>配方名称。</summary>
    [ObservableProperty] private string _recipeName = "";
    /// <summary>显示托盘。</summary>
    [ObservableProperty] private bool _showTray;
    /// <summary>显示档位。</summary>
    [ObservableProperty] private bool _showBins;
    /// <summary>显示NG。</summary>
    [ObservableProperty] private bool _showNg;
    /// <summary>托盘行。</summary>
    [ObservableProperty] private int _trayRows = 1;
    /// <summary>托盘列数。</summary>
    [ObservableProperty] private int _trayCols = 1;
    /// <summary>当前托盘代码。</summary>
    [ObservableProperty] private string? _currentTrayCode;
    /// <summary>托盘单元格数量。</summary>
    [ObservableProperty] private int _trayCellCount;
    /// <summary>总计。</summary>
    [ObservableProperty] private int _total;
    /// <summary>OK。</summary>
    [ObservableProperty] private int _ok;
    /// <summary>NG。</summary>
    [ObservableProperty] private int _ng;
    /// <summary>产出。</summary>
    [ObservableProperty] private double _yield;
    /// <summary>每小时产量。</summary>
    [ObservableProperty] private double _uph;
    /// <summary>OEE。</summary>
    [ObservableProperty] private double _oee;
    /// <summary>可用性。</summary>
    [ObservableProperty] private double _availability;
    /// <summary>性能。</summary>
    [ObservableProperty] private double _performance;
    /// <summary>质量。</summary>
    [ObservableProperty] private double _quality;

    /// <summary>引擎运行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartProductionCommand), nameof(StopProductionCommand))]
    private bool _engineRunning;

    /// <summary>起点生产命令。</summary>
    public IAsyncRelayCommand StartProductionCommand { get; }
    /// <summary>停留生产命令。</summary>
    public IAsyncRelayCommand StopProductionCommand { get; }
    /// <summary>重置计数器命令。</summary>
    public IAsyncRelayCommand ResetCountersCommand { get; }
    /// <summary>重置档位命令。</summary>
    public IAsyncRelayCommand<BinItem> ResetBinCommand { get; }
    /// <summary>触发器流命令。</summary>
    public IAsyncRelayCommand<string> TriggerFlowCommand { get; }
    /// <summary>确认全部命令。</summary>
    public IAsyncRelayCommand AcknowledgeAllCommand { get; }

    /// <summary>处理激活。</summary>
    protected override async Task OnActivatedAsync()
    {
        var layout = _params.Layout;
        MachineTypeName = MachineTypes.DisplayNames.GetValueOrDefault(_params.MachineType, _params.MachineType);
        ShowTray = layout.ShowTray;
        ShowBins = layout.ShowBins;
        ShowNg = layout.ShowNg;
        TrayRows = Math.Max(1, layout.TrayRows);
        TrayCols = Math.Max(1, layout.TrayCols);
        Replace(ManualFlows, layout.ManualFlows);
        Replace(KeyTags, layout.KeyTags.Select(t => _tags.FindTag(t)).Where(t => t != null)
            .Select(t => new TagValueItem { FullName = t!.FullName, Address = t.Address, DataType = t.DataType.ToString() }));
        BuildBins(layout);
        if (ShowTray)
        {
            Replace(TrayCells, Enumerable.Range(1, TrayRows * TrayCols).Select(p => new TrayCellItem { Position = p }));
            if (_trays.CurrentTrayCode is { } code) await LoadTrayAsync(code);
        }
        if (RecentRecords.Count == 0)
        {
            var recent = await _query.QueryAsync(new ProductionQuery(DateTime.Today, DateTime.Now.AddMinutes(1), Limit: 100));
            Replace(RecentRecords, recent);
        }
        await RefreshAsync();
        StartRefresh(TimeSpan.FromSeconds(1), RefreshAsync);
    }

    /// <summary>构建档位。</summary>
    private void BuildBins(MachineLayout layout)
    {
        Bins.Clear();
        if (!layout.ShowBins) return;
        for (var i = 1; i <= Math.Max(1, layout.BinCount); i++)
        {
            Bins.Add(new BinItem { BinNo = i, Name = $"档位 {i}", Capacity = _recipes.GetBoxCapacity(i) });
        }
    }

    /// <summary>刷新。</summary>
    private async Task RefreshAsync()
    {
        RecipeName = _recipes.Current is { } r ? $"{r.Name} v{r.Version}" : "未激活配方";
        EngineRunning = _engine.IsRunning;

        var statuses = _devices.GetStatuses();
        foreach (var s in statuses)
        {
            var item = Devices.FirstOrDefault(d => d.Name == s.DeviceName);
            if (item == null) Devices.Add(item = new DeviceStatusItem { Name = s.DeviceName });
            item.Update(s);
        }
        foreach (var gone in Devices.Where(d => statuses.All(s => s.DeviceName != d.Name)).ToList()) Devices.Remove(gone);

        foreach (var t in KeyTags)
        {
            if (_tags.GetValue(t.FullName) is { } v) t.Update(v);
        }

        var c = _production.Counters;
        Total = c.Total;
        Ok = c.Ok;
        Ng = c.Ng;
        Yield = Math.Round(c.Yield * 100, 2);
        foreach (var bin in Bins)
        {
            bin.Count = c.BinCounts.GetValueOrDefault(bin.BinNo);
            bin.Capacity = _recipes.GetBoxCapacity(bin.BinNo);
            bin.IsFull = bin.Capacity > 0 && bin.Count >= bin.Capacity;
        }
        if (ShowNg)
        {
            var ng = c.NgCounts.OrderByDescending(kv => kv.Value).Select(kv => new NgCountItem(kv.Key, kv.Value)).ToList();
            if (!ng.SequenceEqual(NgCounts)) Replace(NgCounts, ng);
        }

        var active = _alarms.ActiveAlarms;
        if (active.Count != ActiveAlarms.Count || active.Select(a => a.Id).Except(ActiveAlarms.Select(a => a.Id)).Any()) Replace(ActiveAlarms, active);

        var oee = await _query.OeeAsync(_params.GetDouble("IdealCycleSeconds", 2));
        Oee = Math.Round(oee.Oee * 100, 1);
        Availability = Math.Round(oee.Availability * 100, 1);
        Performance = Math.Round(oee.Performance * 100, 1);
        Quality = Math.Round(oee.Quality * 100, 1);
        Uph = Math.Round(oee.Uph, 0);
    }

    /// <summary>加载托盘。</summary>
    private async Task LoadTrayAsync(string trayCode)
    {
        if (!ShowTray) return;
        CurrentTrayCode = trayCode;
        var cells = await _trays.GetCellsAsync(trayCode);
        TrayCellCount = cells.Count;
        foreach (var item in TrayCells)
        {
            var cell = cells.FirstOrDefault(c => c.Position == item.Position);
            item.CellCode = cell?.CellCode;
            item.HasCell = cell != null;
        }
    }

    /// <summary>启动生产。</summary>
    private async Task StartProductionAsync()
    {
        await _engine.StartAsync();
        await _audit.LogAsync(AuditActions.Production, "engine", "停止", "启动");
        EngineRunning = true;
    }

    /// <summary>停止生产。</summary>
    private async Task StopProductionAsync()
    {
        if (!Dialogs.Confirm("停止生产后流程将不再响应 PLC 请求，确定停止？")) return;
        await _engine.StopAsync();
        await _audit.LogAsync(AuditActions.Production, "engine", "运行", "停止");
        EngineRunning = false;
    }

    /// <summary>重置枚举计数器。</summary>
    private async Task ResetCountersAsync()
    {
        if (!Dialogs.Confirm("确定清零全部产量计数？")) return;
        _production.ResetCounters();
        await _audit.LogAsync(AuditActions.Production, "counters", newValue: "清零");
    }

    /// <summary>重置枚举档位。</summary>
    private async Task ResetBinAsync(BinItem? bin)
    {
        if (bin == null) return;
        _production.ResetBin(bin.BinNo);
        await _audit.LogAsync(AuditActions.Production, $"bin:{bin.BinNo}", bin.Count.ToString(), "0");
    }

    /// <summary>触发流。</summary>
    private async Task TriggerFlowAsync(string? code)
    {
        if (string.IsNullOrEmpty(code)) return;
        var i = await _engine.TriggerAsync(code, source: $"Manual:{Context.User.UserName}");
        StatusMessage = i == null ? $"流程 {code} 正在运行，已忽略" : $"流程 {code} 执行结果：{i.Status} {i.Error}";
    }
}
