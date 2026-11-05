using Microsoft.Extensions.Logging;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Expressions;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Flow.Nodes.Builtin;

/// <summary>起点节点。</summary>
[FlowNode("Start", "开始", "逻辑", Description = "流程入口（可选）")]
public sealed class StartNode : INodeHandler
{
    /// <summary>执行。</summary>
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct) => Task.FromResult(NodeResult.Next);
}

/// <summary>端节点。</summary>
[FlowNode("End", "结束", "逻辑", Ports = "", Description = "结束流程，可标记失败")]
[NodeProperty("Status", "结束状态", Kind = NodePropertyKind.Enum, Choices = "Completed,Failed", Default = "Completed")]
[NodeProperty("Message", "消息模板", Kind = NodePropertyKind.Template)]
public sealed class EndNode : INodeHandler
{
    /// <summary>执行。</summary>
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var message = ctx.RenderProp("Message");
        if (ctx.Prop("Status") == "Failed")
        {
            ctx.Instance.Status = FlowInstanceStatus.Failed;
            ctx.Instance.Error = string.IsNullOrEmpty(message) ? "流程以失败状态结束" : message;
        }
        return Task.FromResult(NodeResult.End(message));
    }
}

/// <summary>条件节点。</summary>
[FlowNode("Condition", "条件分支", "逻辑", Ports = "true,false")]
[NodeProperty("Expression", "条件表达式", Kind = NodePropertyKind.Expression, Required = true)]
public sealed class ConditionNode : INodeHandler
{
    /// <summary>执行。</summary>
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct) =>
        Task.FromResult(NodeResult.Bool(ctx.EvaluateBool(ctx.RequireProp("Expression"))));
}

/// <summary>切换节点。</summary>
[FlowNode("Switch", "多路分支", "逻辑", Ports = "default", Description = "按表达式值走 case:值 端口，未匹配走 default")]
[NodeProperty("Expression", "表达式", Kind = NodePropertyKind.Expression, Required = true)]
[NodeProperty("Cases", "分支值", Description = "逗号分隔，如 1,2,3（用于编辑器生成端口）")]
public sealed class SwitchNode : INodeHandler
{
    /// <summary>执行。</summary>
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var value = ScadaExpression.ToInvariantString(ctx.EvalProp("Expression"));
        return Task.FromResult(NodeResult.To($"case:{value}"));
    }
}

/// <summary>循环节点。</summary>
[FlowNode("Loop", "计数循环", "逻辑", Ports = "body,done", Description = "body 分支末端连回本节点形成循环")]
[NodeProperty("Count", "次数表达式", Kind = NodePropertyKind.Expression, Required = true)]
[NodeProperty("Counter", "计数变量", Kind = NodePropertyKind.Variable, Default = "Index", Description = "从 0 开始")]
public sealed class LoopNode : INodeHandler
{
    /// <summary>执行。</summary>
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var stateKey = $"__loop_{ctx.Node.Id}";
        var counter = ctx.Prop("Counter") ?? "Index";
        var index = ctx.GetVar(stateKey) is long l ? l : 0L;
        var count = Convert.ToInt64(ctx.EvalProp("Count") ?? 0, System.Globalization.CultureInfo.InvariantCulture);
        if (index < count)
        {
            ctx.SetVar(counter, index);
            ctx.SetVar(stateKey, index + 1);
            return Task.FromResult(NodeResult.To(Ports.Body));
        }
        ctx.Instance.Variables.TryRemove(stateKey, out _);
        return Task.FromResult(NodeResult.To(Ports.Done));
    }
}

/// <summary>延迟节点。</summary>
[FlowNode("Delay", "延时", "逻辑")]
[NodeProperty("Milliseconds", "毫秒", Kind = NodePropertyKind.Expression, Required = true, Default = "100")]
public sealed class DelayNode : INodeHandler
{
    /// <summary>执行。</summary>
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        await Task.Delay(Math.Max(0, ctx.PropInt("Milliseconds", 100)), ct);
        return NodeResult.Next;
    }
}

/// <summary>集合变量节点。</summary>
[FlowNode("SetVariable", "变量赋值", "逻辑", Description = "每行：变量 = 表达式")]
[NodeProperty("Assignments", "赋值", Kind = NodePropertyKind.Assignments, Required = true)]
[NodeProperty("Scope", "作用域", Kind = NodePropertyKind.Enum, Choices = "Instance,Station,Global", Default = "Instance")]
public sealed class SetVariableNode : INodeHandler
{
    /// <summary>执行。</summary>
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var scope = Enum.TryParse<VariableScope>(ctx.Prop("Scope"), true, out var s) ? s : VariableScope.Instance;
        foreach (var (name, expr) in NodeContext.ParseAssignments(ctx.Prop("Assignments")))
        {
            ctx.SetVar(name, ctx.Evaluate(expr), scope);
        }
        return Task.FromResult(NodeResult.Next);
    }
}

/// <summary>日志节点。</summary>
[FlowNode("Log", "日志", "逻辑")]
[NodeProperty("Message", "消息模板", Kind = NodePropertyKind.Template, Required = true)]
[NodeProperty("Level", "级别", Kind = NodePropertyKind.Enum, Choices = "Information,Warning,Error", Default = "Information")]
public sealed class LogNode : INodeHandler
{
    /// <summary>执行。</summary>
    public Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var message = ctx.RenderProp("Message");
        var level = Enum.TryParse<LogLevel>(ctx.Prop("Level"), out var l) ? l : LogLevel.Information;
        ctx.Logger.Log(level, "[{Flow}] {Message}", ctx.Instance.FlowCode, message);
        return Task.FromResult(NodeResult.To(Ports.Next, message));
    }
}

/// <summary>子级流程节点。</summary>
[FlowNode("SubFlow", "子流程", "逻辑", Ports = "next,error", Description = "调用已发布的流程，传入变量副本")]
[NodeProperty("FlowCode", "流程编码", Kind = NodePropertyKind.FlowRef, Required = true)]
[NodeProperty("Inputs", "输入", Kind = NodePropertyKind.Assignments, Description = "子流程变量 = 表达式；为空时传入全部变量")]
[NodeProperty("Outputs", "返回变量", Description = "逗号分隔，* 表示全部")]
public sealed class SubFlowNode : INodeHandler
{
    /// <summary>执行。</summary>
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var input = string.IsNullOrWhiteSpace(ctx.Prop("Inputs"))
            ? new Dictionary<string, object?>(ctx.Instance.Variables, StringComparer.OrdinalIgnoreCase)
            : ctx.EvalAssignments("Inputs");
        var child = await ctx.Runner.RunSubFlowAsync(ctx.RequireProp("FlowCode"), input, ctx.Instance, ct);
        CopyOutputs(ctx, child);
        if (child.Status != FlowInstanceStatus.Completed) throw new FlowException($"子流程 {child.FlowCode} 失败: {child.Error}");
        return NodeResult.Next;
    }

    /// <summary>复制输出。</summary>
    internal static void CopyOutputs(NodeContext ctx, FlowInstance child)
    {
        var outputs = ctx.Prop("Outputs");
        if (string.IsNullOrWhiteSpace(outputs)) return;
        var names = outputs.Trim() == "*" ? child.Variables.Keys.ToList() : outputs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var n in names)
        {
            if (child.Variables.TryGetValue(n, out var v)) ctx.SetVar(n, v);
        }
    }
}

/// <summary>并行节点。</summary>
[FlowNode("Parallel", "并行子流程", "逻辑", Ports = "next,error", Description = "同时运行多个子流程并等待全部完成")]
[NodeProperty("FlowCodes", "流程编码", Required = true, Description = "逗号分隔")]
[NodeProperty("Outputs", "返回变量", Description = "逗号分隔，* 表示全部；同名变量后完成者覆盖")]
public sealed class ParallelNode : INodeHandler
{
    /// <summary>执行。</summary>
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var codes = ctx.RequireProp("FlowCodes").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var input = new Dictionary<string, object?>(ctx.Instance.Variables, StringComparer.OrdinalIgnoreCase);
        var children = await Task.WhenAll(codes.Select(c => ctx.Runner.RunSubFlowAsync(c, input, ctx.Instance, ct)));
        foreach (var child in children) SubFlowNode.CopyOutputs(ctx, child);
        var failed = children.Where(c => c.Status != FlowInstanceStatus.Completed).ToList();
        if (failed.Count > 0) throw new FlowException("并行子流程失败: " + string.Join("; ", failed.Select(f => $"{f.FlowCode}: {f.Error}")));
        return NodeResult.Next;
    }
}
