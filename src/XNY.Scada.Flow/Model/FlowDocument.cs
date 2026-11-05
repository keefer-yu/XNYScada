using System.Text.Json.Serialization;
using XNY.Scada.Domain;

namespace XNY.Scada.Flow.Model;

/// <summary>流程定义（以 JSON 持久化），一个版本对应一个不可变的 FlowDocument</summary>
public sealed class FlowDocument
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public int Version { get; set; }

    /// <summary>默认工位（变量作用域），触发器可覆盖</summary>
    public string? Station { get; set; }

    public FlowConcurrency Concurrency { get; set; } = FlowConcurrency.Skip;

    /// <summary>整个流程实例超时（毫秒），0 表示不限制</summary>
    public int TimeoutMs { get; set; }

    public string StartNodeId { get; set; } = "";

    public List<TriggerDef> Triggers { get; set; } = [];

    public List<VariableDef> Variables { get; set; } = [];

    public List<NodeDef> Nodes { get; set; } = [];

    public List<EdgeDef> Edges { get; set; } = [];

    public NodeDef? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    public FlowDocument Clone() => Json.Deserialize<FlowDocument>(Json.Serialize(this))!;

    public string ToJson() => Json.Serialize(this);

    public static FlowDocument FromJson(string json) =>
        Json.Deserialize<FlowDocument>(json) ?? throw new InvalidDataException("流程 JSON 为空");
}

public enum FlowConcurrency
{
    /// <summary>运行中再次触发则忽略</summary>
    Skip,
    /// <summary>排队依次执行</summary>
    Queue,
    /// <summary>允许并行执行多个实例</summary>
    Parallel
}

public enum TriggerType
{
    /// <summary>点位上升沿（PLC 请求位置 1）</summary>
    TagRisingEdge,
    TagFallingEdge,
    TagChanged,
    Timer,
    Manual,
    MesCallback,
    /// <summary>其他流程完成后触发</summary>
    FlowCompleted,
    /// <summary>引擎启动时执行一次</summary>
    Startup
}

public sealed class TriggerDef
{
    public TriggerType Type { get; set; }

    /// <summary>点位全名（Tag 类触发）</summary>
    public string? Tag { get; set; }

    /// <summary>附加条件表达式，满足才触发</summary>
    public string? Condition { get; set; }

    public int IntervalMs { get; set; }

    /// <summary>FlowCompleted：来源流程编码；MesCallback：回调路径</summary>
    public string? Source { get; set; }

    public string? Station { get; set; }

    public bool Enabled { get; set; } = true;

    public override string ToString() => Type switch
    {
        TriggerType.TagRisingEdge or TriggerType.TagFallingEdge or TriggerType.TagChanged => $"{Type}:{Tag}",
        TriggerType.Timer => $"Timer:{IntervalMs}ms",
        TriggerType.MesCallback or TriggerType.FlowCompleted => $"{Type}:{Source}",
        _ => Type.ToString()
    };
}

public enum VariableScope
{
    Instance,
    Station,
    Global
}

public sealed class VariableDef
{
    public string Name { get; set; } = "";

    public string? DefaultValue { get; set; }

    public VariableScope Scope { get; set; } = VariableScope.Instance;

    public string? Description { get; set; }
}

public sealed class NodeDef
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    public string Name { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>节点超时（毫秒），0 表示不限</summary>
    public int TimeoutMs { get; set; }

    public Dictionary<string, string> Properties { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Type}#{Id}" : Name;

    public string? Get(string key) => Properties.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

public sealed class EdgeDef
{
    public string From { get; set; } = "";

    /// <summary>输出端口，如 next / true / false / ok / ng / timeout / error / case:A / default</summary>
    public string Port { get; set; } = Ports.Next;

    public string To { get; set; } = "";
}

public static class Ports
{
    public const string Next = "next";
    public const string True = "true";
    public const string False = "false";
    public const string Ok = "ok";
    public const string Ng = "ng";
    public const string Timeout = "timeout";
    public const string Error = "error";
    public const string Default = "default";
    public const string Body = "body";
    public const string Done = "done";
    public const string Yes = "yes";
    public const string No = "no";
    public const string Offline = "offline";
}
