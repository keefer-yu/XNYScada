using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XNY.Scada.Domain;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Nodes;

namespace XNY.Scada.Flow.Runtime;

/// <summary>流程节点事件参数。</summary>
/// <param name="instance">实例。</param>
/// <param name="node">节点。</param>
/// <param name="trace">跟踪。</param>
public sealed class FlowNodeEventArgs(FlowInstance instance, NodeDef node, NodeTrace? trace) : EventArgs
{
    /// <summary>实例。</summary>
    public FlowInstance Instance { get; } = instance;
    /// <summary>节点。</summary>
    public NodeDef Node { get; } = node;
    /// <summary>跟踪。</summary>
    public NodeTrace? Trace { get; } = trace;
}

/// <summary>流运行器的契约。</summary>
public interface IFlowRunner
{
    /// <summary>运行子流程（已发布版本），返回结束后的实例</summary>
    Task<FlowInstance> RunSubFlowAsync(string flowCode, IReadOnlyDictionary<string, object?> input, FlowInstance parent, CancellationToken ct);
}

/// <summary>按节点图逐个执行，记录轨迹，支持节点超时、错误端口与调试暂停</summary>
/// <param name="registry">注册。</param>
/// <param name="services">服务提供程序。</param>
/// <param name="store">存储。</param>
/// <param name="debugger">调试器。</param>
/// <param name="logger">日志记录器。</param>
public sealed class FlowExecutor(
    INodeRegistry registry,
    IServiceProvider services,
    FlowVariableStore store,
    FlowDebugger debugger,
    ILogger<FlowExecutor> logger)
{
    /// <summary>最大步骤。</summary>
    public const int MaxSteps = 100_000;

    /// <summary>节点开始执行发生变化时触发。</summary>
    public event EventHandler<FlowNodeEventArgs>? NodeStarted;

    /// <summary>节点执行结束发生变化时触发。</summary>
    public event EventHandler<FlowNodeEventArgs>? NodeCompleted;

    /// <summary>运行。</summary>
    public async Task RunAsync(FlowInstance instance, IFlowRunner runner, CancellationToken ct)
    {
        var doc = instance.Document;
        using var timeoutCts = doc.TimeoutMs > 0 ? new CancellationTokenSource(doc.TimeoutMs) : null;
        using var linked = timeoutCts == null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linked?.Token ?? ct;

        var node = doc.FindNode(doc.StartNodeId) ?? doc.Nodes.FirstOrDefault();
        var steps = 0;
        try
        {
            while (node != null)
            {
                token.ThrowIfCancellationRequested();
                if (++steps > MaxSteps) throw new FlowException($"流程执行步数超过 {MaxSteps}，可能存在死循环");
                instance.CurrentNodeId = node.Id;
                await debugger.BeforeNodeAsync(instance, node.Id, token);

                var result = await ExecuteNodeAsync(instance, node, runner, token);
                if (result.Terminate) break;

                var next = doc.Edges.FirstOrDefault(e => e.From == node.Id && string.Equals(e.Port, result.Port, StringComparison.OrdinalIgnoreCase));
                if (next == null && result.Port.StartsWith("case:", StringComparison.OrdinalIgnoreCase))
                {
                    next = doc.Edges.FirstOrDefault(e => e.From == node.Id && e.Port == Ports.Default);
                }
                node = next == null ? null : doc.FindNode(next.To) ?? throw new FlowException($"连线指向不存在的节点 {next.To}");
            }
            if (instance.Status == FlowInstanceStatus.Running) instance.Status = FlowInstanceStatus.Completed;
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            instance.Status = FlowInstanceStatus.TimedOut;
            instance.Error = $"流程超时（{doc.TimeoutMs}ms）";
        }
        catch (OperationCanceledException)
        {
            instance.Status = FlowInstanceStatus.Cancelled;
            instance.Error ??= "已取消";
        }
        catch (Exception ex)
        {
            instance.Status = FlowInstanceStatus.Failed;
            instance.Error = ex.Message;
            logger.LogError(ex, "流程 {Flow} 实例 {Id} 在节点 {Node} 失败", doc.Code, instance.Id, instance.CurrentNodeId);
        }
        finally
        {
            instance.EndedAt = DateTime.Now;
        }
    }

    /// <summary>执行节点。</summary>
    private async Task<NodeResult> ExecuteNodeAsync(FlowInstance instance, NodeDef node, IFlowRunner runner, CancellationToken ct)
    {
        var trace = new NodeTrace { NodeId = node.Id, NodeName = node.DisplayName, NodeType = node.Type, StartedAt = DateTime.Now };
        var before = instance.Variables.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        NodeStarted?.Invoke(this, new FlowNodeEventArgs(instance, node, null));
        var sw = Stopwatch.StartNew();
        NodeResult result;
        try
        {
            var handler = registry.Create(node.Type, services);
            var context = new NodeContext(instance, node, services, store, runner, logger);
            if (node.TimeoutMs > 0)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(node.TimeoutMs);
                try
                {
                    result = await handler.ExecuteAsync(context, cts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (!HasPort(instance.Document, node, Ports.Timeout)) throw new FlowException($"节点 {node.DisplayName} 超时（{node.TimeoutMs}ms）");
                    result = NodeResult.To(Ports.Timeout, "节点超时");
                }
            }
            else
            {
                result = await handler.ExecuteAsync(context, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && HasPort(instance.Document, node, Ports.Error))
        {
            instance.Variables["LastError"] = ex.Message;
            trace.Error = ex.Message;
            result = NodeResult.To(Ports.Error, ex.Message);
        }
        catch (Exception ex)
        {
            trace.Error = ex.Message;
            trace.DurationMs = sw.ElapsedMilliseconds;
            instance.AddTrace(trace);
            NodeCompleted?.Invoke(this, new FlowNodeEventArgs(instance, node, trace));
            throw;
        }

        trace.DurationMs = sw.ElapsedMilliseconds;
        trace.Port = result.Port;
        trace.Message = result.Message;
        trace.Changes = Diff(before, instance.Variables);
        instance.AddTrace(trace);
        NodeCompleted?.Invoke(this, new FlowNodeEventArgs(instance, node, trace));
        return result;
    }

    /// <summary>是否有端口号。</summary>
    private static bool HasPort(FlowDocument doc, NodeDef node, string port) =>
        doc.Edges.Any(e => e.From == node.Id && string.Equals(e.Port, port, StringComparison.OrdinalIgnoreCase));

    /// <summary>比较差异。</summary>
    private static Dictionary<string, string?>? Diff(Dictionary<string, object?> before, IReadOnlyDictionary<string, object?> after)
    {
        Dictionary<string, string?>? changes = null;
        foreach (var (k, v) in after)
        {
            if (before.TryGetValue(k, out var old) && Equals(old, v)) continue;
            changes ??= new Dictionary<string, string?>();
            changes[k] = v is string or null || v.GetType().IsPrimitive ? Domain.Expressions.ScadaExpression.ToInvariantString(v) : Json.Serialize(v);
        }
        return changes;
    }
}
