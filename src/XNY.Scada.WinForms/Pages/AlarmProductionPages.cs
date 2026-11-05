using XNY.Scada.Application.Production;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

/// <summary>报警页面。</summary>
public sealed class AlarmPage : UserControl
{
    /// <summary>创建报警页面。</summary>
    public AlarmPage(AlarmViewModel vm)
    {
        var active = new BoundGrid<AlarmRecordEntity>(vm.ActiveAlarms, false,
                new Col("级别", nameof(AlarmRecordEntity.Level), 70),
                new Col("发生时间", nameof(AlarmRecordEntity.RaisedAt), 150, "yyyy-MM-dd HH:mm:ss"),
                new Col("来源", nameof(AlarmRecordEntity.Source), 200),
                Col.Fill("内容", nameof(AlarmRecordEntity.Message)),
                new Col("确认人", nameof(AlarmRecordEntity.AcknowledgedBy), 90))
            .RowColor(a => Colors.Level(a.Level))
            .Selected(vm, nameof(vm.SelectedAlarm));
        var history = new BoundGrid<AlarmRecordEntity>(vm.History, false,
                new Col("级别", nameof(AlarmRecordEntity.Level), 70),
                new Col("发生", nameof(AlarmRecordEntity.RaisedAt), 150, "yyyy-MM-dd HH:mm:ss"),
                new Col("恢复", nameof(AlarmRecordEntity.ClearedAt), 150, "yyyy-MM-dd HH:mm:ss"),
                new Col("确认", nameof(AlarmRecordEntity.AcknowledgedAt), 150, "yyyy-MM-dd HH:mm:ss"),
                new Col("确认人", nameof(AlarmRecordEntity.AcknowledgedBy), 90),
                new Col("来源", nameof(AlarmRecordEntity.Source), 180),
                Col.Fill("内容", nameof(AlarmRecordEntity.Message)))
            .RowColor(a => Colors.Level(a.Level));

        Controls.Add(Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Btn("确认选中", vm.AcknowledgeCommand, ButtonKind.Primary),
                Ui.Btn("全部确认", vm.AcknowledgeAllCommand))),
            ("*", Ui.Card("活动报警", active)),
            ("auto", Ui.Bar(
                Ui.Lbl("从"), Ui.Date(vm, nameof(vm.From)),
                Ui.Lbl("到"), Ui.Date(vm, nameof(vm.To)),
                Ui.Lbl("关键字"), Ui.Text(vm, nameof(vm.Keyword), 160),
                Ui.Btn("查询历史", vm.QueryCommand, ButtonKind.Primary))),
            ("2*", Ui.Card("历史报警", history))));
    }
}

/// <summary>生产页面。</summary>
public sealed class ProductionPage : UserControl
{
    /// <summary>创建生产页面。</summary>
    public ProductionPage(ProductionViewModel vm)
    {
        var records = new BoundGrid<CellRecordEntity>(vm.Records, false,
                new Col("时间", nameof(CellRecordEntity.CreateTime), 150, "yyyy-MM-dd HH:mm:ss"),
                new Col("电芯码", nameof(CellRecordEntity.CellCode), 180),
                new Col("托盘", nameof(CellRecordEntity.TrayCode), 120),
                new Col("位置", nameof(CellRecordEntity.Position), 50),
                new Col("电压", nameof(CellRecordEntity.Voltage), 70),
                new Col("内阻", nameof(CellRecordEntity.InternalResistance), 70),
                new Col("容量", nameof(CellRecordEntity.Capacity), 70),
                new Col("档位", nameof(CellRecordEntity.BinName), 80),
                new Col("OK", nameof(CellRecordEntity.IsOk), 45),
                new Col("NG 代码", nameof(CellRecordEntity.NgCode), 90),
                new Col("配方", nameof(CellRecordEntity.RecipeCode), 90),
                new Col("工位", nameof(CellRecordEntity.Station), 70),
                new Col("操作员", nameof(CellRecordEntity.Operator), 80),
                Col.Fill("扩展", nameof(CellRecordEntity.ExtraJson)))
            .RowColor(r => r.IsOk ? null : Ui.Danger);
        var ng = new BoundGrid<NgRecordEntity>(vm.NgRecords, false,
            new Col("时间", nameof(NgRecordEntity.CreateTime), 150, "yyyy-MM-dd HH:mm:ss"),
            new Col("电芯码", nameof(NgRecordEntity.CellCode), 180),
            new Col("NG 代码", nameof(NgRecordEntity.NgCode), 100),
            new Col("料盒", nameof(NgRecordEntity.BoxNo), 60),
            Col.Fill("原因", nameof(NgRecordEntity.NgReason)));
        var hourly = new BoundGrid<HourlyOutput>(vm.Hourly, false,
            new Col("时段", nameof(HourlyOutput.Hour), 160, "yyyy-MM-dd HH:00"),
            new Col("产量", nameof(HourlyOutput.Total), 100),
            new Col("OK", nameof(HourlyOutput.Ok), 100),
            new Col("NG", nameof(HourlyOutput.Ng), 100));
        var trayCells = new BoundGrid<TrayCellEntity>(vm.TrayCells, false,
            new Col("位置", nameof(TrayCellEntity.Position), 60),
            new Col("电芯码", nameof(TrayCellEntity.CellCode), 220),
            new Col("绑定时间", nameof(TrayCellEntity.BindTime), 160, "yyyy-MM-dd HH:mm:ss"),
            Col.Button("解绑", vm.UnbindCellCommand));

        var traceBox = Ui.Text(vm, nameof(vm.TraceCode), 220);
        traceBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Bind.Execute(vm.TraceCommand);
        };
        var trace = Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Lbl("电芯码 / 托盘码"), traceBox,
                Ui.Btn("追溯", vm.TraceCommand, ButtonKind.Primary),
                Ui.Btn("整盘解绑", vm.UnbindTrayCommand, ButtonKind.Danger),
                Ui.Show(vm, nameof(vm.TraceResult)))),
            ("*", trayCells));

        var totals = Ui.Bar(
            Ui.Lbl("合计"), Ui.Show(vm, nameof(vm.Total), bold: true),
            Ui.Lbl("   OK"), Ui.Show(vm, nameof(vm.OkCount), bold: true, color: Ui.Good),
            Ui.Lbl("   NG"), Ui.Show(vm, nameof(vm.NgCount), bold: true, color: Ui.Danger));

        Controls.Add(Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Lbl("从"), Ui.Date(vm, nameof(vm.From)),
                Ui.Lbl("到"), Ui.Date(vm, nameof(vm.To)),
                Ui.Lbl("电芯码"), Ui.Text(vm, nameof(vm.CellCode), 150),
                Ui.Lbl("托盘码"), Ui.Text(vm, nameof(vm.TrayCode), 130),
                Ui.Lbl("结果"), Ui.Combo(vm.ResultFilters, vm, nameof(vm.ResultFilter), 80),
                Ui.Btn("查询", vm.QueryCommand, ButtonKind.Primary),
                Ui.Btn("导出 CSV", vm.ExportCommand))),
            ("auto", totals),
            ("*", Ui.Tabs(("生产记录", records), ("NG 记录", ng), ("小时产量", hourly), ("托盘追溯", trace)))));
    }
}
