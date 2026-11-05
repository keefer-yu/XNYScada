using System.Collections.Concurrent;
using XNY.Scada.Domain;
using XNY.Scada.Flow.Model;

namespace XNY.Scada.Flow.Runtime;

/// <summary>节点跟踪。</summary>
public sealed class NodeTrace
{
    /// <summary>节点标识。</summary>
    public string NodeId { get; set; } = "";
    /// <summary>节点名称。</summary>
    public string NodeName { get; set; } = "";
    /// <summary>节点类型。</summary>
    public string NodeType { get; set; } = "";
    /// <summary>已启动于。</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>时长毫秒。</summary>
    public long DurationMs { get; set; }
    /// <summary>输出端口。</summary>
    public string? Port { get; set; }
    /// <summary>消息。</summary>
    public string? Message { get; set; }
    /// <summary>错误。</summary>
    public string? Error { get; set; }

    /// <summary>节点执行后变化的变量</summary>
    public Dictionary<string, string?>? Changes { get; set; }
}

/// <summary>一次流程运行实例</summary>
public sealed class FlowInstance
{
    /// <summary>跟踪。</summary>
    private readonly List<NodeTrace> _trace = [];

    /// <summary>创建流实例。</summary>
    public FlowInstance(FlowDocument document, string triggerSource, string? station, IReadOnlyDictionary<string, object?>? input, bool isSimulation)
    {
        Document = document;
        TriggerSource = triggerSource;
        Station = station ?? document.Station;
        IsSimulation = isSimulation;
        foreach (var v in document.Variables.Where(v => v.Scope == VariableScope.Instance))
        {
            Variables[v.Name] = v.DefaultValue;
        }
        if (input != null)
        {
            foreach (var kv in input) Variables[kv.Key] = kv.Value;
        }
    }

    /// <summary>流实例标识。</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>文档。</summary>
    public FlowDocument Document { get; }

    /// <summary>流代码。</summary>
    public string FlowCode => Document.Code;

    /// <summary>触发器源。</summary>
    public string TriggerSource { get; }

    /// <summary>工位。</summary>
    public string? Station { get; }

    /// <summary>是否模拟。</summary>
    public bool IsSimulation { get; }

    /// <summary>已启动于。</summary>
    public DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>已结束于。</summary>
    public DateTime? EndedAt { get; internal set; }

    /// <summary>流实例状态。</summary>
    public FlowInstanceStatus Status { get; internal set; } = FlowInstanceStatus.Running;

    /// <summary>错误。</summary>
    public string? Error { get; internal set; }

    /// <summary>当前节点标识。</summary>
    public string? CurrentNodeId { get; internal set; }

    /// <summary>变量声明。</summary>
    public ConcurrentDictionary<string, object?> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>跟踪。</summary>
    public IReadOnlyList<NodeTrace> Trace
    {
        get { lock (_trace) return _trace.ToList(); }
    }

    /// <summary>添加跟踪。</summary>
    internal void AddTrace(NodeTrace t)
    {
        lock (_trace)
        {
            if (_trace.Count < 2000) _trace.Add(t);
        }
    }

    /// <summary>时长毫秒。</summary>
    public long DurationMs => (long)((EndedAt ?? DateTime.Now) - StartedAt).TotalMilliseconds;

    /// <summary>跟踪JSON。</summary>
    public string TraceJson() => Json.Serialize(Trace);
}
