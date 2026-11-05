using System.Globalization;
using System.Text.RegularExpressions;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Expressions;
using XNY.Scada.Domain.Services;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Mes;

namespace XNY.Scada.Flow.Nodes.Builtin;

[FlowNode("ScanBarcode", "扫码", "业务", Ports = "ok,ng", Description = "（可选）触发扫码枪后从点位读取条码并校验，失败可重试")]
[NodeProperty("Tag", "条码点位", Kind = NodePropertyKind.Tag, Required = true, Order = 0)]
[NodeProperty("TriggerTag", "扫码触发位", Kind = NodePropertyKind.Tag, Description = "可选：写 true 触发扫码", Order = 1)]
[NodeProperty("ReadDelayMs", "读取延时(ms)", Kind = NodePropertyKind.Int, Default = "200", Order = 2)]
[NodeProperty("Retries", "重试次数", Kind = NodePropertyKind.Int, Default = "2", Order = 3)]
[NodeProperty("Variable", "条码变量", Kind = NodePropertyKind.Variable, Default = "Barcode", Order = 4)]
[NodeProperty("Regex", "格式正则", Description = "为空不校验", Order = 5)]
[NodeProperty("MinLength", "最小长度", Kind = NodePropertyKind.Int, Default = "1", Order = 6)]
public sealed class ScanBarcodeNode : INodeHandler
{
    private static readonly string[] NoRead = ["NOREAD", "ERROR", "NG", "NONE"];

    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var tag = ctx.Render(ctx.RequireProp("Tag"));
        var trigger = ctx.Prop("TriggerTag") is { } t ? ctx.Render(t) : null;
        var variable = ctx.Prop("Variable") ?? "Barcode";
        var retries = Math.Max(0, ctx.PropInt("Retries", 2));
        string message = "";
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            if (trigger != null)
            {
                await ctx.WriteTagAsync(trigger, false, ct);
                await ctx.WriteTagAsync(trigger, true, ct);
            }
            await Task.Delay(Math.Max(0, ctx.PropInt("ReadDelayMs", 200)), ct);
            var code = Convert.ToString(await ctx.ReadTagAsync(tag, true, ct), CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (trigger != null) await ctx.WriteTagAsync(trigger, false, ct);
            message = BarcodeCheckNode.Check(code, ctx.Prop("Regex"), ctx.PropInt("MinLength", 1));
            if (message.Length == 0 && NoRead.Contains(code, StringComparer.OrdinalIgnoreCase)) message = $"扫码失败: {code}";
            ctx.SetVar(variable, code);
            if (message.Length == 0) return NodeResult.OkNg(true);
        }
        ctx.SetVar("NgReason", message);
        return NodeResult.OkNg(false, message);
    }
}

[FlowNode("BarcodeCheck", "条码校验", "业务", Ports = "ok,ng", Description = "校验格式、长度、重码（已生产过）、已绑定托盘")]
[NodeProperty("Value", "条码表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "Barcode")]
[NodeProperty("Regex", "格式正则")]
[NodeProperty("MinLength", "最小长度", Kind = NodePropertyKind.Int, Default = "1")]
[NodeProperty("CheckDuplicate", "校验重码", Kind = NodePropertyKind.Bool, Default = "false")]
[NodeProperty("CheckBound", "校验已绑盘", Kind = NodePropertyKind.Bool, Default = "false")]
public sealed class BarcodeCheckNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var code = ScadaExpression.ToInvariantString(ctx.EvalProp("Value")).Trim();
        var message = Check(code, ctx.Prop("Regex"), ctx.PropInt("MinLength", 1));
        if (message.Length == 0 && ctx.PropBool("CheckDuplicate", false) && await ctx.Get<IProductionService>().CellProcessedAsync(code))
            message = $"重码：{code} 已生产过";
        if (message.Length == 0 && ctx.PropBool("CheckBound", false) && await ctx.Get<ITrayService>().FindTrayOfCellAsync(code) is { } tray)
            message = $"{code} 已绑定托盘 {tray}";
        if (message.Length > 0) ctx.SetVar("NgReason", message);
        return NodeResult.OkNg(message.Length == 0, message);
    }

    public static string Check(string code, string? regex, int minLength)
    {
        if (code.Length < Math.Max(1, minLength)) return $"条码长度不足: '{code}'";
        if (!string.IsNullOrWhiteSpace(regex) && !Regex.IsMatch(code, regex)) return $"条码格式不符: {code}";
        return "";
    }
}

[FlowNode("BindTray", "电芯绑定托盘", "业务", Ports = "ok,ng", Description = "绑定后输出 TrayCellCount / TrayFull")]
[NodeProperty("TrayCode", "托盘号表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "TrayCode")]
[NodeProperty("Position", "位置表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "Position")]
[NodeProperty("CellCode", "电芯码表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "Barcode")]
[NodeProperty("Rows", "托盘行数", Kind = NodePropertyKind.Expression, Default = "{Recipe:TrayRows}")]
[NodeProperty("Cols", "托盘列数", Kind = NodePropertyKind.Expression, Default = "{Recipe:TrayCols}")]
public sealed class BindTrayNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var trays = ctx.Get<ITrayService>();
        var trayCode = ScadaExpression.ToInvariantString(ctx.EvalProp("TrayCode"));
        var rows = ToInt(ctx.EvalProp("Rows"), 1);
        var cols = ToInt(ctx.EvalProp("Cols"), 1);
        await trays.GetOrCreateTrayAsync(trayCode, rows, cols);
        var r = await trays.BindCellAsync(trayCode, ToInt(ctx.EvalProp("Position"), 0), ScadaExpression.ToInvariantString(ctx.EvalProp("CellCode")));
        ctx.SetVar("TrayCellCount", (long)r.CellCount);
        ctx.SetVar("TrayFull", r.TrayFull);
        if (!r.Success) ctx.SetVar("NgReason", r.Message);
        return NodeResult.OkNg(r.Success, r.Message);
    }

    internal static int ToInt(object? v, int defaultValue) =>
        v == null || (v is string s && string.IsNullOrWhiteSpace(s)) ? defaultValue : Convert.ToInt32(v, CultureInfo.InvariantCulture);
}

[FlowNode("CompleteTray", "托盘完成", "业务", Description = "标记托盘满盘/完成")]
[NodeProperty("TrayCode", "托盘号表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "TrayCode")]
public sealed class CompleteTrayNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        await ctx.Get<ITrayService>().CompleteTrayAsync(ScadaExpression.ToInvariantString(ctx.EvalProp("TrayCode")));
        return NodeResult.Next;
    }
}

[FlowNode("LoadTray", "查询托盘电芯", "业务", Ports = "ok,ng", Description = "输出 TrayCells(列表)、TrayCellCount、Cell_{位置} 变量")]
[NodeProperty("TrayCode", "托盘号表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "TrayCode")]
public sealed class LoadTrayNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var code = ScadaExpression.ToInvariantString(ctx.EvalProp("TrayCode"));
        var cells = await ctx.Get<ITrayService>().GetCellsAsync(code);
        ctx.SetVar("TrayCells", cells.Select(c => c.CellCode).ToList());
        ctx.SetVar("TrayCellCount", (long)cells.Count);
        foreach (var c in cells) ctx.SetVar($"Cell_{c.Position}", c.CellCode);
        if (cells.Count == 0) ctx.SetVar("NgReason", $"托盘 {code} 无绑定电芯");
        return NodeResult.OkNg(cells.Count > 0);
    }
}

[FlowNode("UnbindTray", "托盘解绑", "业务", Description = "Position 为空时整盘解绑，输出 UnboundCount")]
[NodeProperty("TrayCode", "托盘号表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "TrayCode")]
[NodeProperty("Position", "位置表达式", Kind = NodePropertyKind.Expression)]
public sealed class UnbindTrayNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var position = ctx.Prop("Position") == null ? (int?)null : BindTrayNode.ToInt(ctx.EvalProp("Position"), 0);
        var n = await ctx.Get<ITrayService>().UnbindAsync(ScadaExpression.ToInvariantString(ctx.EvalProp("TrayCode")), position);
        ctx.SetVar("UnboundCount", (long)n);
        return NodeResult.Next;
    }
}

[FlowNode("CalcGrade", "分档计算", "业务", Ports = "ok,ng", Description = "按当前配方分档规则计算档位，输出 BinNo/BinName/IsNg/NgCode")]
[NodeProperty("Inputs", "输入数据", Kind = NodePropertyKind.Assignments, Required = true, Description = "Voltage = V\nInternalResistance = IR\nCapacity = Cap")]
[NodeProperty("RecipeCode", "配方编码", Description = "为空使用当前配方")]
public sealed class CalcGradeNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var inputs = ctx.EvalAssignments("Inputs");
        var r = await ctx.Get<IGradingService>().GradeAsync(inputs, ctx.Prop("RecipeCode"));
        ctx.SetVar("BinNo", (long)r.BinNo);
        ctx.SetVar("BinName", r.BinName);
        ctx.SetVar("IsNg", r.IsNg || !r.Matched);
        ctx.SetVar("NgCode", r.NgCode);
        var ok = r.Matched && !r.IsNg;
        if (!ok) ctx.SetVar("NgReason", r.Message ?? $"NG: {r.NgCode}");
        return NodeResult.OkNg(ok, r.Message);
    }
}

[FlowNode("NgJudge", "NG 判定", "业务", Ports = "ok,ng", Description = "根据 NG 代码映射到 NG 料盒，输出 BoxNo/NgCode")]
[NodeProperty("NgCode", "NG 代码表达式", Kind = NodePropertyKind.Expression, Required = true, Description = "空或 OK 表示良品")]
[NodeProperty("MapParam", "料盒映射配方参数", Default = "NgBoxMap", Description = "配方参数，JSON 对象：{\"NG01\":1}")]
[NodeProperty("DefaultBox", "默认料盒", Kind = NodePropertyKind.Int, Default = "1")]
public sealed class NgJudgeNode : INodeHandler
{
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var code = ScadaExpression.ToInvariantString(ctx.EvalProp("NgCode")).Trim();
        ctx.SetVar("NgCode", code);
        if (code.Length == 0 || code.Equals("OK", StringComparison.OrdinalIgnoreCase) || code == "0")
        {
            ctx.SetVar("BoxNo", 0L);
            return Task.FromResult(NodeResult.OkNg(true));
        }
        var box = (long)ctx.PropInt("DefaultBox", 1);
        var map = ctx.TryGet<IRecipeProvider>()?.GetParam(ctx.Prop("MapParam") ?? "NgBoxMap");
        if (map is IDictionary<string, object?> dict && dict.TryGetValue(code, out var b) && b != null)
            box = Convert.ToInt64(b, CultureInfo.InvariantCulture);
        ctx.SetVar("BoxNo", box);
        return Task.FromResult(NodeResult.OkNg(false, $"NG {code} → 料盒 {box}"));
    }
}

[FlowNode("SaveCellRecord", "保存生产记录", "业务", Description = "已知字段：CellCode TrayCode Position Voltage InternalResistance Capacity BinNo BinName IsOk NgCode，其余写入扩展字段")]
[NodeProperty("Fields", "字段", Kind = NodePropertyKind.Assignments, Required = true)]
public sealed class SaveCellRecordNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var f = ctx.EvalAssignments("Fields");
        object? Take(string key) => f.Remove(key, out var v) ? v : null;
        double? D(object? v) => v == null || v is string { Length: 0 } ? null : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        int? I(object? v) => v == null || v is string { Length: 0 } ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        string? S(object? v) => v == null ? null : ScadaExpression.ToInvariantString(v);

        var record = new CellRecordEntity
        {
            CellCode = S(Take("CellCode")) ?? "",
            TrayCode = S(Take("TrayCode")),
            Position = I(Take("Position")),
            Voltage = D(Take("Voltage")),
            InternalResistance = D(Take("InternalResistance")),
            Capacity = D(Take("Capacity")),
            BinNo = I(Take("BinNo")),
            BinName = S(Take("BinName")),
            IsOk = Take("IsOk") is { } ok && Device.TagValueConverter.ToBool(ok),
            NgCode = S(Take("NgCode")),
            Station = ctx.Instance.Station,
            RecipeCode = ctx.TryGet<IRecipeProvider>()?.Current?.Code,
            MachineType = S(ctx.TryGet<IFlowEnvironment>()?.GetParam("MachineType")),
            ExtraJson = f.Count > 0 ? Json.Serialize(f) : null
        };
        if (!ctx.Instance.IsSimulation) await ctx.Get<IProductionService>().SaveCellRecordAsync(record);
        return NodeResult.Next;
    }
}

[FlowNode("SaveNgRecord", "保存 NG 记录", "业务")]
[NodeProperty("CellCode", "电芯码表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "Barcode")]
[NodeProperty("NgCode", "NG 代码表达式", Kind = NodePropertyKind.Expression, Required = true, Default = "NgCode")]
[NodeProperty("NgReason", "原因模板", Kind = NodePropertyKind.Template, Default = "${NgReason}")]
[NodeProperty("BoxNo", "料盒表达式", Kind = NodePropertyKind.Expression, Default = "BoxNo")]
public sealed class SaveNgRecordNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var record = new NgRecordEntity
        {
            CellCode = ScadaExpression.ToInvariantString(ctx.EvalProp("CellCode")),
            NgCode = ScadaExpression.ToInvariantString(ctx.EvalProp("NgCode")),
            NgReason = ctx.RenderProp("NgReason"),
            BoxNo = BindTrayNode.ToInt(ctx.Prop("BoxNo") == null ? null : ctx.EvalProp("BoxNo"), 0),
            Station = ctx.Instance.Station
        };
        if (!ctx.Instance.IsSimulation) await ctx.Get<IProductionService>().SaveNgRecordAsync(record);
        return NodeResult.Next;
    }
}

[FlowNode("MesCall", "调用 MES", "业务", Ports = "ok,ng,offline", Description = "调用配置的 MES 接口；输出 {前缀}Success/{前缀}Message 与响应映射字段")]
[NodeProperty("InterfaceCode", "接口编码", Kind = NodePropertyKind.MesRef, Required = true)]
[NodeProperty("Args", "参数", Kind = NodePropertyKind.Assignments, Description = "模板变量 = 表达式；为空时传入全部流程变量")]
[NodeProperty("OutputPrefix", "输出变量前缀", Default = "Mes")]
public sealed class MesCallNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var args = string.IsNullOrWhiteSpace(ctx.Prop("Args"))
            ? new Dictionary<string, object?>(ctx.Instance.Variables, StringComparer.OrdinalIgnoreCase)
            : ctx.EvalAssignments("Args");
        var prefix = ctx.Prop("OutputPrefix") ?? "Mes";
        var r = await ctx.Get<IMesClient>().CallAsync(ctx.RequireProp("InterfaceCode"), args, ct);
        ctx.SetVar($"{prefix}Success", r.Success);
        ctx.SetVar($"{prefix}Message", r.Message);
        foreach (var (k, v) in r.Fields) ctx.SetVar($"{prefix}{k}", v);
        if (!r.Success) ctx.SetVar("NgReason", r.Message);
        if (r.Offline && !r.Queued) return NodeResult.To(Ports.Offline, r.Message);
        return NodeResult.OkNg(r.Success, r.Message);
    }
}

[FlowNode("RaiseAlarm", "触发报警", "业务")]
[NodeProperty("Source", "报警源", Required = true, Description = "同一报警源未清除前不重复报警")]
[NodeProperty("Message", "报警内容模板", Kind = NodePropertyKind.Template, Required = true)]
[NodeProperty("Level", "级别", Kind = NodePropertyKind.Enum, Choices = "Info,Warning,Error,Critical", Default = "Warning")]
public sealed class RaiseAlarmNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var level = Enum.TryParse<AlarmLevel>(ctx.Prop("Level"), true, out var l) ? l : AlarmLevel.Warning;
        await ctx.Get<IAlarmService>().RaiseAsync(ctx.Render(ctx.RequireProp("Source")), ctx.RenderProp("Message"), level);
        return NodeResult.Next;
    }
}

[FlowNode("ClearAlarm", "清除报警", "业务")]
[NodeProperty("Source", "报警源", Required = true)]
public sealed class ClearAlarmNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        await ctx.Get<IAlarmService>().ClearAsync(ctx.Render(ctx.RequireProp("Source")));
        return NodeResult.Next;
    }
}

[FlowNode("Confirm", "人工确认", "业务", Ports = "yes,no", Description = "弹窗等待操作员确认，超时取默认结果")]
[NodeProperty("Title", "标题", Default = "请确认")]
[NodeProperty("Message", "内容模板", Kind = NodePropertyKind.Template, Required = true)]
[NodeProperty("TimeoutMs", "超时(ms)", Kind = NodePropertyKind.Int, Default = "60000")]
[NodeProperty("Default", "超时默认", Kind = NodePropertyKind.Bool, Default = "false")]
public sealed class ConfirmNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var ui = ctx.TryGet<IUserInteraction>() ?? new NullUserInteraction();
        var yes = await ui.ConfirmAsync(ctx.Prop("Title") ?? "请确认", ctx.RenderProp("Message"),
            TimeSpan.FromMilliseconds(Math.Max(1000, ctx.PropInt("TimeoutMs", 60000))), ctx.PropBool("Default", false), ct);
        return NodeResult.To(yes ? Ports.Yes : Ports.No);
    }
}
