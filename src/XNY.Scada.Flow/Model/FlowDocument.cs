using System.Text.Json.Serialization;
using XNY.Scada.Domain;

namespace XNY.Scada.Flow.Model;

/// <summary>流程定义（以 JSON 持久化），一个版本对应一个不可变的 FlowDocument</summary>
public sealed class FlowDocument
{
    /// <summary>流文档编码。</summary>
    public string Code { get; set; } = "";

    /// <summary>流文档名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>流文档说明。</summary>
    public string? Description { get; set; }

    /// <summary>版本号。</summary>
    public int Version { get; set; }

    /// <summary>默认工位（变量作用域），触发器可覆盖</summary>
    public string? Station { get; set; }

    /// <summary>并发策略。</summary>
    public FlowConcurrency Concurrency { get; set; } = FlowConcurrency.Skip;

    /// <summary>整个流程实例超时（毫秒），0 表示不限制</summary>
    public int TimeoutMs { get; set; }

    /// <summary>起始节点标识。</summary>
    public string StartNodeId { get; set; } = "";

    /// <summary>触发器。</summary>
    public List<TriggerDef> Triggers { get; set; } = [];

    /// <summary>变量声明。</summary>
    public List<VariableDef> Variables { get; set; } = [];

    /// <summary>节点。</summary>
    public List<NodeDef> Nodes { get; set; } = [];

    /// <summary>连线。</summary>
    public List<EdgeDef> Edges { get; set; } = [];

    /// <summary>查找节点。</summary>
    public NodeDef? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>克隆副本。</summary>
    public FlowDocument Clone() => Json.Deserialize<FlowDocument>(Json.Serialize(this))!;

    /// <summary>转换为JSON。</summary>
    public string ToJson() => Json.Serialize(this);

    /// <summary>从源创建JSON。</summary>
    public static FlowDocument FromJson(string json) =>
        Json.Deserialize<FlowDocument>(json) ?? throw new InvalidDataException("流程 JSON 为空");
}

/// <summary>流并发策略。</summary>
public enum FlowConcurrency
{
    /// <summary>运行中再次触发则忽略</summary>
    Skip,
    /// <summary>排队依次执行</summary>
    Queue,
    /// <summary>允许并行执行多个实例</summary>
    Parallel
}

/// <summary>触发器类型。</summary>
public enum TriggerType
{
    /// <summary>点位上升沿（PLC 请求位置 1）</summary>
    TagRisingEdge,
    /// <summary>点位下降沿。</summary>
    TagFallingEdge,
    /// <summary>点位值变化。</summary>
    TagChanged,
    /// <summary>定时。</summary>
    Timer,
    /// <summary>手动。</summary>
    Manual,
    /// <summary>MES 回调。</summary>
    MesCallback,
    /// <summary>其他流程完成后触发</summary>
    FlowCompleted,
    /// <summary>引擎启动时执行一次</summary>
    Startup
}

/// <summary>流程触发器。同一流程可配置多个触发器，Enabled 为 false 时不参与运行。</summary>
public sealed class TriggerDef
{
    /// <summary>触发器定义类型。</summary>
    public TriggerType Type { get; set; }

    /// <summary>点位全名（Tag 类触发）</summary>
    public string? Tag { get; set; }

    /// <summary>附加条件表达式，满足才触发</summary>
    public string? Condition { get; set; }

    /// <summary>定时间隔（毫秒）。</summary>
    public int IntervalMs { get; set; }

    /// <summary>FlowCompleted：来源流程编码；MesCallback：回调路径</summary>
    public string? Source { get; set; }

    /// <summary>工位。</summary>
    public string? Station { get; set; }

    /// <summary>是否触发器定义是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>转换为字符串。</summary>
    public override string ToString() => Type switch
    {
        TriggerType.TagRisingEdge or TriggerType.TagFallingEdge or TriggerType.TagChanged => $"{Type}:{Tag}",
        TriggerType.Timer => $"Timer:{IntervalMs}ms",
        TriggerType.MesCallback or TriggerType.FlowCompleted => $"{Type}:{Source}",
        _ => Type.ToString()
    };
}

/// <summary>变量范围。</summary>
public enum VariableScope
{
    /// <summary>仅当前流程实例。</summary>
    Instance,
    /// <summary>同一工位共享。</summary>
    Station,
    /// <summary>全局共享。</summary>
    Global
}

/// <summary>流程变量声明。实例变量随一次运行销毁，工位与全局变量跨实例保留。</summary>
public sealed class VariableDef
{
    /// <summary>变量定义名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>默认值。</summary>
    public string? DefaultValue { get; set; }

    /// <summary>作用域。</summary>
    public VariableScope Scope { get; set; } = VariableScope.Instance;

    /// <summary>变量定义说明。</summary>
    public string? Description { get; set; }
}

/// <summary>流程节点。Type 对应节点注册表中的类型，Properties 为设计器保存的参数。</summary>
public sealed class NodeDef
{
    /// <summary>节点定义标识。</summary>
    public string Id { get; set; } = "";

    /// <summary>节点定义类型。</summary>
    public string Type { get; set; } = "";

    /// <summary>节点定义名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>设计器中的横坐标。</summary>
    public double X { get; set; }

    /// <summary>设计器中的纵坐标。</summary>
    public double Y { get; set; }

    /// <summary>节点超时（毫秒），0 表示不限</summary>
    public int TimeoutMs { get; set; }

    /// <summary>节点参数。</summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>显示名。</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Type}#{Id}" : Name;

    /// <summary>获取。</summary>
    public string? Get(string key) => Properties.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

/// <summary>节点之间的连线。Port 决定从哪个输出端口离开。</summary>
public sealed class EdgeDef
{
    /// <summary>起点节点。</summary>
    public string From { get; set; } = "";

    /// <summary>输出端口，如 next / true / false / ok / ng / timeout / error / case:A / default</summary>
    public string Port { get; set; } = Ports.Next;

    /// <summary>目标节点。</summary>
    public string To { get; set; } = "";
}

/// <summary>内置节点使用的输出端口名。自定义节点应复用这些名称，便于设计器识别。</summary>
public static class Ports
{
    /// <summary>默认下一节点。</summary>
    public const string Next = "next";
    /// <summary>条件为真。</summary>
    public const string True = "true";
    /// <summary>条件为假。</summary>
    public const string False = "false";
    /// <summary>成功。</summary>
    public const string Ok = "ok";
    /// <summary>不合格。</summary>
    public const string Ng = "ng";
    /// <summary>超时。</summary>
    public const string Timeout = "timeout";
    /// <summary>出错。</summary>
    public const string Error = "error";
    /// <summary>默认分支。</summary>
    public const string Default = "default";
    /// <summary>循环体。</summary>
    public const string Body = "body";
    /// <summary>循环结束。</summary>
    public const string Done = "done";
    /// <summary>确认。</summary>
    public const string Yes = "yes";
    /// <summary>取消。</summary>
    public const string No = "no";
    /// <summary>离线。</summary>
    public const string Offline = "offline";
}
