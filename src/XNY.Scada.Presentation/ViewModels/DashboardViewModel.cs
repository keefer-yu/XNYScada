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

public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IDeviceManager _devices;
    private readonly ITagService _tags;
    private readonly IProductionService _production;
    private readonly ProductionQueryService _query;
    private readonly ITrayService _trays;
    private readonly RecipeService _recipes;
    private readonly SystemParamService _params;
    private readonly IFlowEngine _engine;
    private readonly IAlarmService _alarms;
    private readonly IAuditService _audit;

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

    public override string Title => "主监控";

    public ObservableCollection<DeviceStatusItem> Devices { get; } = [];
    public ObservableCollection<TrayCellItem> TrayCells { get; } = [];
    public ObservableCollection<BinItem> Bins { get; } = [];
    public ObservableCollection<NgCountItem> NgCounts { get; } = [];
    public ObservableCollection<TagValueItem> KeyTags { get; } = [];
    public ObservableCollection<CellRecordEntity> RecentRecords { get; } = [];
    public ObservableCollection<AlarmRecordEntity> ActiveAlarms { get; } = [];
    public ObservableCollection<string> ManualFlows { get; } = [];

    [ObservableProperty] private string _machineTypeName = "";
    [ObservableProperty] private string _recipeName = "";
    [ObservableProperty] private bool _showTray;
    [ObservableProperty] private bool _showBins;
    [ObservableProperty] private bool _showNg;
    [ObservableProperty] private int _trayRows = 1;
    [ObservableProperty] private int _trayCols = 1;
    [ObservableProperty] private string? _currentTrayCode;
    [ObservableProperty] private int _trayCellCount;
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _ok;
    [ObservableProperty] private int _ng;
    [ObservableProperty] private double _yield;
    [ObservableProperty] private double _uph;
    [ObservableProperty] private double _oee;
    [ObservableProperty] private double _availability;
    [ObservableProperty] private double _performance;
    [ObservableProperty] private double _quality;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartProductionCommand), nameof(StopProductionCommand))]
    private bool _engineRunning;

    public IAsyncRelayCommand StartProductionCommand { get; }
    public IAsyncRelayCommand StopProductionCommand { get; }
    public IAsyncRelayCommand ResetCountersCommand { get; }
    public IAsyncRelayCommand<BinItem> ResetBinCommand { get; }
    public IAsyncRelayCommand<string> TriggerFlowCommand { get; }
    public IAsyncRelayCommand AcknowledgeAllCommand { get; }

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

    private void BuildBins(MachineLayout layout)
    {
        Bins.Clear();
        if (!layout.ShowBins) return;
        for (var i = 1; i <= Math.Max(1, layout.BinCount); i++)
        {
            Bins.Add(new BinItem { BinNo = i, Name = $"档位 {i}", Capacity = _recipes.GetBoxCapacity(i) });
        }
    }

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

    private async Task StartProductionAsync()
    {
        await _engine.StartAsync();
        await _audit.LogAsync(AuditActions.Production, "engine", "停止", "启动");
        EngineRunning = true;
    }

    private async Task StopProductionAsync()
    {
        if (!Dialogs.Confirm("停止生产后流程将不再响应 PLC 请求，确定停止？")) return;
        await _engine.StopAsync();
        await _audit.LogAsync(AuditActions.Production, "engine", "运行", "停止");
        EngineRunning = false;
    }

    private async Task ResetCountersAsync()
    {
        if (!Dialogs.Confirm("确定清零全部产量计数？")) return;
        _production.ResetCounters();
        await _audit.LogAsync(AuditActions.Production, "counters", newValue: "清零");
    }

    private async Task ResetBinAsync(BinItem? bin)
    {
        if (bin == null) return;
        _production.ResetBin(bin.BinNo);
        await _audit.LogAsync(AuditActions.Production, $"bin:{bin.BinNo}", bin.Count.ToString(), "0");
    }

    private async Task TriggerFlowAsync(string? code)
    {
        if (string.IsNullOrEmpty(code)) return;
        var i = await _engine.TriggerAsync(code, source: $"Manual:{Context.User.UserName}");
        StatusMessage = i == null ? $"流程 {code} 正在运行，已忽略" : $"流程 {code} 执行结果：{i.Status} {i.Error}";
    }
}
