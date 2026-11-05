using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

/// <summary>配方页面。</summary>
public sealed class RecipePage : UserControl
{
    /// <summary>创建配方页面。</summary>
    public RecipePage(RecipeViewModel vm)
    {
        var recipes = new BoundGrid<RecipeEntity>(vm.Recipes, false,
                new Col("编码", nameof(RecipeEntity.Code), 90),
                Col.Fill("名称", nameof(RecipeEntity.Name)),
                new Col("版本", nameof(RecipeEntity.Version), 45),
                new Col("状态", nameof(RecipeEntity.Status), 100),
                new Col("生产", nameof(RecipeEntity.IsActive), 45))
            .RowColor(r => r.IsActive ? Ui.Good : null)
            .Selected(vm, nameof(vm.Selected));
        var left = Ui.Rows(
            ("auto", Ui.Bar(Ui.Lbl("生产中："), Ui.Show(vm, nameof(vm.CurrentRecipe), bold: true, color: Ui.Good))),
            ("auto", Ui.Bar(
                Ui.Btn("新建", vm.NewCommand),
                Ui.Btn("复制", vm.CopyCommand),
                Ui.Btn("删除", vm.DeleteCommand, ButtonKind.Danger))),
            ("*", recipes));

        var header = Ui.Stack(
            Ui.Bar(
                Ui.Lbl("编码"), Ui.Text(vm, nameof(vm.Code), 140),
                Ui.Lbl("名称"), Ui.Text(vm, nameof(vm.Name), 180),
                Ui.Lbl("产品型号"), Ui.Text(vm, nameof(vm.ProductModel), 140),
                Ui.Show(vm, nameof(vm.StatusText), color: Ui.Primary)),
            Ui.Bar(
                Ui.Btn("保存草稿", vm.SaveCommand, ButtonKind.Primary),
                Ui.Btn("提交审批", vm.SubmitCommand),
                Ui.Btn("审批（签名）", vm.ApproveCommand),
                Ui.Btn("投入生产（签名）", vm.ActivateCommand)));

        var rules = new BoundGrid<GradingRuleRow>(vm.Rules, true,
            Col.Edit("优先级", nameof(GradingRuleRow.Priority), 55),
            Col.Edit("档位号", nameof(GradingRuleRow.BinNo), 55),
            Col.Edit("档位名", nameof(GradingRuleRow.BinName), 90),
            Col.Fill("区间条件", nameof(GradingRuleRow.Conditions), 200) with { Editable = true },
            Col.Fill("附加表达式", nameof(GradingRuleRow.Expression)) with { Editable = true },
            Col.Edit("满箱数", nameof(GradingRuleRow.BoxCapacity), 60),
            Col.Edit("NG", nameof(GradingRuleRow.IsNg), 40),
            Col.Edit("NG 代码", nameof(GradingRuleRow.NgCode), 80),
            Col.Button("删", vm.RemoveRuleCommand, 44));
        var parameters = new BoundGrid<RecipeParamRow>(vm.Params, true,
            Col.Edit("名称", nameof(RecipeParamRow.Name), 200),
            Col.Fill("值", nameof(RecipeParamRow.Value)) with { Editable = true },
            Col.Button("删", vm.RemoveParamCommand, 44));

        var right = Ui.Rows(
            ("auto", Ui.Card("配方", header)),
            ("*", Ui.Card("分档规则（条件格式：字段:下限~上限; 多个用分号分隔，空表示不限）",
                Ui.Rows(("auto", Ui.Bar(Ui.Btn("添加档位", vm.AddRuleCommand))), ("*", rules)))),
            ("200", Ui.Card("配方参数（流程中以 {Recipe:名称} 引用）",
                Ui.Rows(("auto", Ui.Bar(Ui.Btn("添加参数", vm.AddParamCommand))), ("*", parameters)))));

        Controls.Add(Ui.Cols(("340", Ui.Card("配方列表", left)), ("*", right)));
    }
}

/// <summary>流程监控页面。</summary>
public sealed class FlowMonitorPage : UserControl
{
    /// <summary>创建流程监控页面。</summary>
    public FlowMonitorPage(FlowMonitorViewModel vm)
    {
        var dot = new Label { Text = "●", AutoSize = true, Font = new Font(Font.FontFamily, 12f), Margin = new Padding(0, 4, 2, 0) };
        Bind.Observe(vm, nameof(vm.EngineRunning), () => dot.ForeColor = vm.EngineRunning ? Ui.Good : Color.Gray);
        var toolbar = Ui.Bar(
            dot, Ui.Lbl("引擎"),
            Ui.Btn("重载全部流程", vm.ReloadAllCommand),
            Ui.Check("调试模式", vm, nameof(vm.DebugEnabled)),
            Ui.Check("单步", vm, nameof(vm.StepMode)),
            Ui.Btn("全部继续", vm.ContinueAllCommand),
            Ui.Btn("清除断点", vm.ClearBreakpointsCommand),
            Ui.Lbl("断点："), Ui.Show(vm, nameof(vm.BreakpointsText), color: Ui.Danger));

        var flows = new BoundGrid<FlowRuntimeItem>(vm.Flows, false,
                new Col("编码", nameof(FlowRuntimeItem.Code), 120),
                Col.Fill("名称", nameof(FlowRuntimeItem.Name)),
                new Col("版本", nameof(FlowRuntimeItem.VersionText), 110),
                new Col("启用", nameof(FlowRuntimeItem.Enabled), 45),
                new Col("运行中", nameof(FlowRuntimeItem.RunningCount), 55),
                new Col("完成", nameof(FlowRuntimeItem.CompletedCount), 60),
                new Col("失败", nameof(FlowRuntimeItem.FailedCount), 50),
                new Col("最近运行", nameof(FlowRuntimeItem.LastRunAt), 75, "HH:mm:ss"),
                new Col("最近错误", nameof(FlowRuntimeItem.LastError), 160),
                Col.Button("触发", vm.TriggerCommand, 50),
                Col.Button("启/停", vm.ToggleEnabledCommand, 50))
            .RowColor(f => f.Enabled ? null : Color.Gray)
            .Selected(vm, nameof(vm.SelectedFlow));
        var flowCard = Ui.Card("已发布流程", Ui.Rows(
            ("auto", Ui.Bar(Ui.Lbl("触发输入 JSON"), Ui.Text(vm, nameof(vm.TriggerInput), 260))),
            ("*", flows)));

        var instances = new BoundGrid<RunningInstanceItem>(vm.Instances, false,
                new Col("流程", nameof(RunningInstanceItem.FlowCode), 90),
                new Col("实例", nameof(RunningInstanceItem.Id), 95),
                Col.Fill("当前节点", nameof(RunningInstanceItem.CurrentNode)),
                new Col("暂停", nameof(RunningInstanceItem.Paused), 40),
                Col.Button("继续", vm.ContinueCommand, 50),
                Col.Button("取消", vm.CancelInstanceCommand, 50))
            .RowColor(i => i.Paused ? Ui.Warn : null);

        var events = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 18 };
        events.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= vm.Events.Count) return;
            var line = vm.Events[e.Index];
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, $"{line.Time:HH:mm:ss.fff} {line.FlowCode} {line.Text}", events.Font, e.Bounds,
                line.IsError ? Ui.Danger : e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        Bind.Observe(vm.Events, () =>
        {
            events.BeginUpdate();
            while (events.Items.Count < vm.Events.Count) events.Items.Add("");
            while (events.Items.Count > vm.Events.Count) events.Items.RemoveAt(events.Items.Count - 1);
            events.EndUpdate();
            events.Invalidate();
        });
        var eventCard = Ui.Card("实时事件", Ui.Rows(
            ("auto", Ui.Bar(Ui.Check("实时", vm, nameof(vm.LiveEvents)), Ui.Btn("清空", vm.ClearEventsCommand))),
            ("*", events)));

        var logs = new BoundGrid<FlowInstanceLogEntity>(vm.Logs, false,
                new Col("开始", nameof(FlowInstanceLogEntity.StartedAt), 140, "MM-dd HH:mm:ss.fff"),
                new Col("流程", nameof(FlowInstanceLogEntity.FlowCode), 100),
                new Col("版本", nameof(FlowInstanceLogEntity.Version), 45),
                new Col("状态", nameof(FlowInstanceLogEntity.Status), 90),
                new Col("耗时ms", nameof(FlowInstanceLogEntity.DurationMs), 70),
                new Col("触发", nameof(FlowInstanceLogEntity.TriggerSource), 150),
                Col.Fill("错误", nameof(FlowInstanceLogEntity.Error)))
            .RowColor(l => Colors.Status(l.Status))
            .Selected(vm, nameof(vm.SelectedLog));
        var logCard = Ui.Card("执行历史（选中流程）", Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Date(vm, nameof(vm.LogFrom)), Ui.Date(vm, nameof(vm.LogTo)),
                Ui.Combo(vm.StatusFilters, vm, nameof(vm.LogStatus), 100),
                Ui.Btn("查询", vm.QueryLogsCommand, ButtonKind.Primary))),
            ("*", logs)));

        var trace = new BoundGrid<NodeTrace>(vm.SelectedTrace, false,
                Col.Fill("节点", nameof(NodeTrace.NodeName)),
                new Col("端口", nameof(NodeTrace.Port), 70),
                new Col("ms", nameof(NodeTrace.DurationMs), 50),
                Col.Fill("信息", nameof(NodeTrace.Message)),
                Col.Fill("错误", nameof(NodeTrace.Error)))
            .RowColor(t => t.Error != null ? Ui.Danger : null);

        Controls.Add(Ui.Rows(
            ("auto", toolbar),
            ("*", Ui.Cols(("3*", flowCard), ("2*", Ui.Rows(("*", Ui.Card("运行中实例", instances)), ("2*", eventCard))))),
            ("*", Ui.Cols(("3*", logCard), ("2*", Ui.Card("节点轨迹", trace))))));
    }
}
