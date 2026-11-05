using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Production;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>生产页面的视图模型。</summary>
public sealed partial class ProductionViewModel : ViewModelBase
{
    /// <summary>查询。</summary>
    private readonly ProductionQueryService _query;
    /// <summary>托盘。</summary>
    private readonly ITrayService _trays;
    /// <summary>审计。</summary>
    private readonly IAuditService _audit;

    /// <summary>创建生产视图模型。</summary>
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

    /// <summary>标题。</summary>
    public override string Title => "生产数据";

    /// <summary>结果筛选条件。</summary>
    public IReadOnlyList<string> ResultFilters { get; } = ["全部", "OK", "NG"];

    /// <summary>记录。</summary>
    public ObservableCollection<CellRecordEntity> Records { get; } = [];
    /// <summary>NG记录。</summary>
    public ObservableCollection<NgRecordEntity> NgRecords { get; } = [];
    /// <summary>每小时。</summary>
    public ObservableCollection<HourlyOutput> Hourly { get; } = [];
    /// <summary>托盘单元格。</summary>
    public ObservableCollection<TrayCellEntity> TrayCells { get; } = [];

    /// <summary>从。</summary>
    [ObservableProperty] private DateTime _from = DateTime.Today;
    /// <summary>到。</summary>
    [ObservableProperty] private DateTime _to = DateTime.Today.AddDays(1);
    /// <summary>单元格代码。</summary>
    [ObservableProperty] private string? _cellCode;
    /// <summary>托盘代码。</summary>
    [ObservableProperty] private string? _trayCode;
    /// <summary>结果筛选。</summary>
    [ObservableProperty] private string _resultFilter = "全部";
    /// <summary>总计。</summary>
    [ObservableProperty] private int _total;
    /// <summary>OK数量。</summary>
    [ObservableProperty] private int _okCount;
    /// <summary>NG数量。</summary>
    [ObservableProperty] private int _ngCount;
    /// <summary>跟踪代码。</summary>
    [ObservableProperty] private string? _traceCode;
    /// <summary>跟踪结果。</summary>
    [ObservableProperty] private string? _traceResult;
    /// <summary>已跟踪托盘代码。</summary>
    [ObservableProperty] private string? _tracedTrayCode;

    /// <summary>查询命令。</summary>
    public IAsyncRelayCommand QueryCommand { get; }
    /// <summary>导出命令。</summary>
    public IAsyncRelayCommand ExportCommand { get; }
    /// <summary>跟踪命令。</summary>
    public IAsyncRelayCommand TraceCommand { get; }
    /// <summary>解绑单元格命令。</summary>
    public IAsyncRelayCommand<TrayCellEntity> UnbindCellCommand { get; }
    /// <summary>解绑托盘命令。</summary>
    public IAsyncRelayCommand UnbindTrayCommand { get; }

    /// <summary>处理激活。</summary>
    protected override Task OnActivatedAsync() => QueryAsync();

    /// <summary>构建查询。</summary>
    private ProductionQuery BuildQuery() => new(From, To,
        string.IsNullOrWhiteSpace(CellCode) ? null : CellCode.Trim(),
        string.IsNullOrWhiteSpace(TrayCode) ? null : TrayCode.Trim(),
        ResultFilter switch { "OK" => true, "NG" => false, _ => null });

    /// <summary>查询。</summary>
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

    /// <summary>导出。</summary>
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

    /// <summary>解绑单元格。</summary>
    private async Task UnbindCellAsync(TrayCellEntity? cell)
    {
        if (cell == null || TracedTrayCode == null) return;
        if (!Dialogs.Confirm($"解绑托盘 {TracedTrayCode} 位置 {cell.Position} 的电芯 {cell.CellCode}？")) return;
        await _trays.UnbindAsync(TracedTrayCode, cell.Position);
        await _audit.LogAsync(AuditActions.Production, $"tray:{TracedTrayCode}:{cell.Position}", cell.CellCode, "解绑");
        await TraceAsync();
    }

    /// <summary>解绑托盘。</summary>
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
