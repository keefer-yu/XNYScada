using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Flow.Nodes;

/// <summary>流程节点处理器。实现类需标注 <see cref="FlowNodeAttribute"/>，属性通过 <see cref="NodePropertyAttribute"/> 声明。</summary>
public interface INodeHandler
{
    Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct);
}

public sealed record NodeResult(string Port, bool Terminate = false, string? Message = null)
{
    public static readonly NodeResult Next = new(Ports.Next);

    public static NodeResult To(string port, string? message = null) => new(port, false, message);

    public static NodeResult End(string? message = null) => new(Ports.Next, true, message);

    public static NodeResult Bool(bool value) => new(value ? Ports.True : Ports.False);

    public static NodeResult OkNg(bool ok, string? message = null) => new(ok ? Ports.Ok : Ports.Ng, false, message);
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FlowNodeAttribute(string type, string displayName, string category) : Attribute
{
    public string Type { get; } = type;

    public string DisplayName { get; } = displayName;

    public string Category { get; } = category;

    /// <summary>输出端口，逗号分隔；动态端口（如 Switch）在运行时由属性决定</summary>
    public string Ports { get; set; } = Model.Ports.Next;

    public string Description { get; set; } = "";

    /// <summary>编辑该节点需要的权限码，为空表示 Flow.Edit 即可</summary>
    public string? RequiredPermission { get; set; }
}

public enum NodePropertyKind
{
    Text,
    /// <summary>NCalc 表达式</summary>
    Expression,
    /// <summary>文本模板，支持 ${var} 与 {Kind:Name}</summary>
    Template,
    Tag,
    Variable,
    Int,
    Bool,
    Enum,
    /// <summary>多行赋值：每行 名称 = 表达式</summary>
    Assignments,
    Code,
    FlowRef,
    MesRef
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class NodePropertyAttribute(string key, string displayName) : Attribute
{
    public string Key { get; } = key;

    public string DisplayName { get; } = displayName;

    public NodePropertyKind Kind { get; set; } = NodePropertyKind.Text;

    public bool Required { get; set; }

    public string Default { get; set; } = "";

    public string Choices { get; set; } = "";

    public string Description { get; set; } = "";

    public int Order { get; set; }
}

public sealed record NodePropertyDescriptor(
    string Key, string DisplayName, NodePropertyKind Kind, bool Required, string Default,
    IReadOnlyList<string> Choices, string Description, int Order);

public sealed record NodeDescriptor(
    string Type, string DisplayName, string Category, string Description,
    IReadOnlyList<string> Ports, IReadOnlyList<NodePropertyDescriptor> Properties,
    string? RequiredPermission, Type HandlerType);
