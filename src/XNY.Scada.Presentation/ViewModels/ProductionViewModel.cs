using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Production;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class ProductionViewModel : ViewModelBase
{
    private readonly ProductionQueryService _query;
    private readonly ITrayService _trays;
    private readonly IAuditService _audit;

    public ProductionViewModel(ViewModelContext context, ProductionQueryService query, ITrayService trays, IAuditService audit) : base(context)
    {
        _query = query;
        _trays = trays;
        _audit = audit;
        QueryCommand = Command(QueryAsync);
        ExportCommand = Secured(Permissions.Production.Export, ExportAsync);
        TraceCommand = Command(TraceAsync);
        UnbindCellCommand = Secured<TrayCellEntity>(Permissions.Production.TrayManual, UnbindCellAsync);
        UnbindTrayCommand = Secured(Permissions.Production.TrayManual, UnbindTrayAsync);
    }

    public override string Title => "生产数据";

    public IReadOnlyList<string> ResultFilters { get; } = ["全部", "OK", "NG"];

    public ObservableCollection<CellRecordEntity> Records { get; } = [];
    public ObservableCollection<NgRecordEntity> NgRecords { get; } = [];
    public ObservableCollection<HourlyOutput> Hourly { get; } = [];
    public ObservableCollection<TrayCellEntity> TrayCells { get; } = [];

    [ObservableProperty] private DateTime _from = DateTime.Today;
    [ObservableProperty] private DateTime _to = DateTime.Today.AddDays(1);
    [ObservableProperty] private string? _cellCode;
    [ObservableProperty] private string? _trayCode;
    [ObservableProperty] private string _resultFilter = "全部";
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _okCount;
    [ObservableProperty] private int _ngCount;
    [ObservableProperty] private string? _traceCode;
    [ObservableProperty] private string? _traceResult;
    [ObservableProperty] private string? _tracedTrayCode;

    public IAsyncRelayCommand QueryCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand TraceCommand { get; }
    public IAsyncRelayCommand<TrayCellEntity> UnbindCellCommand { get; }
    public IAsyncRelayCommand UnbindTrayCommand { get; }

    protected override Task OnActivatedAsync() => QueryAsync();

    private ProductionQuery BuildQuery() => new(From, To,
        string.IsNullOrWhiteSpace(CellCode) ? null : CellCode.Trim(),
        string.IsNullOrWhiteSpace(TrayCode) ? null : TrayCode.Trim(),
        ResultFilter switch { "OK" => true, "NG" => false, _ => null });

    private async Task QueryAsync()
    {
        if (From >= To) throw new InvalidOperationException("开始时间必须早于结束时间");
        var records = await _query.QueryAsync(BuildQuery());
        Replace(Records, records);
        Total = records.Count;
        OkCount = records.Count(r => r.IsOk);
        NgCount = Total - OkCount;
        Replace(NgRecords, await _query.QueryNgAsync(From, To, string.IsNullOrWhiteSpace(CellCode) ? null : CellCode.Trim()));
        Replace(Hourly, await _query.HourlyAsync(From, To));
    }

    private async Task ExportAsync()
    {
        var path = Dialogs.SaveFile("CSV 文件|*.csv", $"production_{From:yyyyMMdd}_{To:yyyyMMdd}.csv");
        if (path == null) return;
        var n = await _query.ExportCsvAsync(BuildQuery() with { Limit = 1_000_000 }, path);
        await _audit.LogAsync(AuditActions.Production, "export", newValue: $"{n} 条 -> {path}");
        Dialogs.ShowInfo($"已导出 {n} 条记录");
    }

    /// <summary>追溯：输入电芯码或托盘码，显示所在托盘及托盘内全部电芯</summary>
    private async Task TraceAsync()
    {
        var code = TraceCode?.Trim();
        if (string.IsNullOrEmpty(code)) return;
        var trayCode = await _trays.GetTrayAsync(code) != null ? code : await _trays.FindTrayOfCellAsync(code);
        if (trayCode == null)
        {
            TracedTrayCode = null;
            TrayCells.Clear();
            TraceResult = $"未找到 {code} 的托盘绑定关系";
            return;
        }
        var tray = await _trays.GetTrayAsync(trayCode);
        TracedTrayCode = trayCode;
        Replace(TrayCells, await _trays.GetCellsAsync(trayCode));
        TraceResult = $"托盘 {trayCode}  状态 {tray?.Status}  电芯数 {TrayCells.Count}";
    }

    private async Task UnbindCellAsync(TrayCellEntity? cell)
    {
        if (cell == null || TracedTrayCode == null) return;
        if (!Dialogs.Confirm($"解绑托盘 {TracedTrayCode} 位置 {cell.Position} 的电芯 {cell.CellCode}？")) return;
        await _trays.UnbindAsync(TracedTrayCode, cell.Position);
        await _audit.LogAsync(AuditActions.Production, $"tray:{TracedTrayCode}:{cell.Position}", cell.CellCode, "解绑");
        await TraceAsync();
    }

    private async Task UnbindTrayAsync()
    {
        if (TracedTrayCode == null) return;
        var signer = await SignAsync(Permissions.Production.TrayManual, $"整盘解绑 {TracedTrayCode}");
        if (signer == null) return;
        var n = await _trays.UnbindAsync(TracedTrayCode, null);
        await _audit.LogAsync(AuditActions.Production, $"tray:{TracedTrayCode}", $"{n} 颗", "整盘解绑", signer);
        await TraceAsync();
    }
}
