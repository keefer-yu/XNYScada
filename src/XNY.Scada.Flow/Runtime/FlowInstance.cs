using System.Collections.Concurrent;
using XNY.Scada.Domain;
using XNY.Scada.Flow.Model;

namespace XNY.Scada.Flow.Runtime;

public sealed class NodeTrace
{
    public string NodeId { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string NodeType { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public long DurationMs { get; set; }
    public string? Port { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }

    /// <summary>节点执行后变化的变量</summary>
    public Dictionary<string, string?>? Changes { get; set; }
}

/// <summary>一次流程运行实例</summary>
public sealed class FlowInstance
{
    private readonly List<NodeTrace> _trace = [];

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

    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];

    public FlowDocument Document { get; }

    public string FlowCode => Document.Code;

    public string TriggerSource { get; }

    public string? Station { get; }

    public bool IsSimulation { get; }

    public DateTime StartedAt { get; } = DateTime.Now;

    public DateTime? EndedAt { get; internal set; }

    public FlowInstanceStatus Status { get; internal set; } = FlowInstanceStatus.Running;

    public string? Error { get; internal set; }

    public string? CurrentNodeId { get; internal set; }

    public ConcurrentDictionary<string, object?> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<NodeTrace> Trace
    {
        get { lock (_trace) return _trace.ToList(); }
    }

    internal void AddTrace(NodeTrace t)
    {
        lock (_trace)
        {
            if (_trace.Count < 2000) _trace.Add(t);
        }
    }

    public long DurationMs => (long)((EndedAt ?? DateTime.Now) - StartedAt).TotalMilliseconds;

    public string TraceJson() => Json.Serialize(Trace);
}
