using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Flow.Nodes;

/// <summary>流程节点处理器。实现类需标注 <see cref="FlowNodeAttribute"/>，属性通过 <see cref="NodePropertyAttribute"/> 声明。</summary>
public interface INodeHandler
{
    /// <summary>执行。</summary>
    Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct);
}

/// <summary>节点结果。</summary>
/// <param name="Port">输出端口。</param>
/// <param name="Terminate">终止。</param>
/// <param name="Message">消息。</param>
public sealed record NodeResult(string Port, bool Terminate = false, string? Message = null)
{
    /// <summary>下一步。</summary>
    public static readonly NodeResult Next = new(Ports.Next);

    /// <summary>目标节点。</summary>
    public static NodeResult To(string port, string? message = null) => new(port, false, message);

    /// <summary>结束。</summary>
    public static NodeResult End(string? message = null) => new(Ports.Next, true, message);

    /// <summary>布尔。</summary>
    public static NodeResult Bool(bool value) => new(value ? Ports.True : Ports.False);

    /// <summary>OKNG。</summary>
    public static NodeResult OkNg(bool ok, string? message = null) => new(ok ? Ports.Ok : Ports.Ng, false, message);
}

/// <summary>标记流程节点的特性。</summary>
/// <param name="type">类型。</param>
/// <param name="displayName">显示名称。</param>
/// <param name="category">分类。</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FlowNodeAttribute(string type, string displayName, string category) : Attribute
{
    /// <summary>流程节点类型。</summary>
    public string Type { get; } = type;

    /// <summary>显示名。</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>分类。</summary>
    public string Category { get; } = category;

    /// <summary>输出端口，逗号分隔；动态端口（如 Switch）在运行时由属性决定</summary>
    public string Ports { get; set; } = Model.Ports.Next;

    /// <summary>流程节点说明。</summary>
    public string Description { get; set; } = "";

    /// <summary>编辑该节点需要的权限码，为空表示 Flow.Edit 即可</summary>
    public string? RequiredPermission { get; set; }
}

/// <summary>节点属性类别。</summary>
public enum NodePropertyKind
{
    /// <summary>文本。</summary>
    Text,
    /// <summary>NCalc 表达式</summary>
    Expression,
    /// <summary>文本模板，支持 ${var} 与 {Kind:Name}</summary>
    Template,
    /// <summary>标记。</summary>
    Tag,
    /// <summary>变量。</summary>
    Variable,
    /// <summary>整数。</summary>
    Int,
    /// <summary>布尔。</summary>
    Bool,
    /// <summary>枚举。</summary>
    Enum,
    /// <summary>多行赋值：每行 名称 = 表达式</summary>
    Assignments,
    /// <summary>编码。</summary>
    Code,
    /// <summary>流引用。</summary>
    FlowRef,
    /// <summary>MES引用。</summary>
    MesRef
}

/// <summary>标记节点属性的特性。</summary>
/// <param name="key">键。</param>
/// <param name="displayName">显示名称。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class NodePropertyAttribute(string key, string displayName) : Attribute
{
    /// <summary>节点属性键。</summary>
    public string Key { get; } = key;

    /// <summary>显示名。</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>节点属性种类。</summary>
    public NodePropertyKind Kind { get; set; } = NodePropertyKind.Text;

    /// <summary>是否必填。</summary>
    public bool Required { get; set; }

    /// <summary>默认。</summary>
    public string Default { get; set; } = "";

    /// <summary>可选值。</summary>
    public string Choices { get; set; } = "";

    /// <summary>节点属性说明。</summary>
    public string Description { get; set; } = "";

    /// <summary>显示顺序。</summary>
    public int Order { get; set; }
}

/// <summary>节点属性描述。</summary>
/// <param name="Key">键。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Kind">种类。</param>
/// <param name="Required">是否必填。</param>
/// <param name="Default">默认。</param>
/// <param name="Choices">可选值。</param>
/// <param name="Description">说明。</param>
/// <param name="Order">显示顺序。</param>
public sealed record NodePropertyDescriptor(
    string Key, string DisplayName, NodePropertyKind Kind, bool Required, string Default,
    IReadOnlyList<string> Choices, string Description, int Order);

/// <summary>节点描述。</summary>
/// <param name="Type">类型。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Category">分类。</param>
/// <param name="Description">说明。</param>
/// <param name="Ports">输出端口。</param>
/// <param name="Properties">节点参数。</param>
/// <param name="RequiredPermission">编辑该节点所需权限。</param>
/// <param name="HandlerType">处理器类型。</param>
public sealed record NodeDescriptor(
    string Type, string DisplayName, string Category, string Description,
    IReadOnlyList<string> Ports, IReadOnlyList<NodePropertyDescriptor> Properties,
    string? RequiredPermission, Type HandlerType);
