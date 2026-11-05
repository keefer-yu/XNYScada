using XNY.Scada.Device;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Flow.Nodes.Builtin;

[FlowNode("ReadTag", "读点位", "通讯", Description = "从 PLC 读取点位值到变量")]
[NodeProperty("Tag", "点位", Kind = NodePropertyKind.Tag, Required = true, Order = 0)]
[NodeProperty("Variable", "保存到变量", Kind = NodePropertyKind.Variable, Required = true, Order = 1)]
[NodeProperty("FromDevice", "直接读设备", Kind = NodePropertyKind.Bool, Default = "true", Description = "false 时读取轮询缓存", Order = 2)]
public sealed class ReadTagNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var tag = ctx.Render(ctx.RequireProp("Tag"));
        ctx.SetVar(ctx.RequireProp("Variable"), await ctx.ReadTagAsync(tag, ctx.PropBool("FromDevice", true), ct));
        return NodeResult.Next;
    }
}

[FlowNode("ReadTags", "批量读点位", "通讯", Description = "每行：变量 = 点位全名")]
[NodeProperty("Mappings", "映射", Kind = NodePropertyKind.Assignments, Required = true, Description = "Voltage = Dev1.Voltage")]
[NodeProperty("FromDevice", "直接读设备", Kind = NodePropertyKind.Bool, Default = "true")]
public sealed class ReadTagsNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var fromDevice = ctx.PropBool("FromDevice", true);
        foreach (var (variable, tag) in NodeContext.ParseAssignments(ctx.Prop("Mappings")))
        {
            ctx.SetVar(variable, await ctx.ReadTagAsync(ctx.Render(tag), fromDevice, ct));
        }
        return NodeResult.Next;
    }
}

[FlowNode("WriteTag", "写点位", "通讯", Description = "表达式结果写入 PLC")]
[NodeProperty("Tag", "点位", Kind = NodePropertyKind.Tag, Required = true, Order = 0)]
[NodeProperty("Value", "值表达式", Kind = NodePropertyKind.Expression, Required = true, Order = 1)]
public sealed class WriteTagNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        await ctx.WriteTagAsync(ctx.Render(ctx.RequireProp("Tag")), ctx.EvalProp("Value"), ct);
        return NodeResult.Next;
    }
}

[FlowNode("WriteTags", "批量写点位", "通讯", Description = "每行：点位全名 = 值表达式，按顺序写入")]
[NodeProperty("Mappings", "映射", Kind = NodePropertyKind.Assignments, Required = true, Description = "Dev1.BinNo = BinNo")]
public sealed class WriteTagsNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        foreach (var (tag, expr) in NodeContext.ParseAssignments(ctx.Prop("Mappings")))
        {
            await ctx.WriteTagAsync(ctx.Render(tag), ctx.Evaluate(expr), ct);
        }
        return NodeResult.Next;
    }
}

[FlowNode("WaitSignal", "等待信号", "通讯", Ports = "next,timeout", Description = "轮询直到条件成立或超时")]
[NodeProperty("Condition", "条件表达式", Kind = NodePropertyKind.Expression, Required = true, Description = "如 {Tag:PLC.Ready} == true", Order = 0)]
[NodeProperty("Timeout", "超时(ms)", Kind = NodePropertyKind.Int, Default = "10000", Description = "0 表示一直等待", Order = 1)]
[NodeProperty("PollMs", "轮询周期(ms)", Kind = NodePropertyKind.Int, Default = "20", Order = 2)]
public sealed class WaitSignalNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var condition = ctx.RequireProp("Condition");
        var timeout = ctx.PropInt("Timeout", 10000);
        var poll = Math.Max(5, ctx.PropInt("PollMs", 20));
        var deadline = timeout > 0 ? DateTime.Now.AddMilliseconds(timeout) : DateTime.MaxValue;
        while (true)
        {
            if (ctx.EvaluateBool(condition)) return NodeResult.Next;
            if (DateTime.Now >= deadline) return NodeResult.To(Ports.Timeout, $"等待超时: {condition}");
            await Task.Delay(poll, ct);
        }
    }
}

/// <summary>
/// 标准 PLC 握手收尾：上位置完成位 → 等待 PLC 复位请求位 → 上位复位完成位。
/// 典型时序：PLC 置 Request → 上位读数/处理/写结果 → 本节点。
/// </summary>
[FlowNode("Handshake", "PLC 握手完成", "通讯", Ports = "next,timeout", Description = "置完成位，等待 PLC 清请求位后复位完成位")]
[NodeProperty("RequestTag", "请求位(PLC→上位)", Kind = NodePropertyKind.Tag, Required = true, Order = 0)]
[NodeProperty("DoneTag", "完成位(上位→PLC)", Kind = NodePropertyKind.Tag, Required = true, Order = 1)]
[NodeProperty("ResultTag", "结果点位", Kind = NodePropertyKind.Tag, Description = "可选：完成前写入结果", Order = 2)]
[NodeProperty("Result", "结果表达式", Kind = NodePropertyKind.Expression, Order = 3)]
[NodeProperty("Timeout", "等待复位超时(ms)", Kind = NodePropertyKind.Int, Default = "5000", Order = 4)]
public sealed class HandshakeNode : INodeHandler
{
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var request = ctx.Render(ctx.RequireProp("RequestTag"));
        var done = ctx.Render(ctx.RequireProp("DoneTag"));
        if (ctx.Prop("ResultTag") is { } resultTag && ctx.Prop("Result") != null)
        {
            await ctx.WriteTagAsync(ctx.Render(resultTag), ctx.EvalProp("Result"), ct);
        }
        await ctx.WriteTagAsync(done, true, ct);

        var timeout = ctx.PropInt("Timeout", 5000);
        var deadline = DateTime.Now.AddMilliseconds(timeout <= 0 ? int.MaxValue : timeout);
        var cleared = false;
        while (DateTime.Now < deadline)
        {
            if (!TagValueConverter.ToBool(ctx.Tags.GetValue(request)?.Value))
            {
                cleared = true;
                break;
            }
            await Task.Delay(10, ct);
        }
        await ctx.WriteTagAsync(done, false, ct);
        return cleared ? NodeResult.Next : NodeResult.To(Ports.Timeout, "PLC 未复位请求位");
    }
}
