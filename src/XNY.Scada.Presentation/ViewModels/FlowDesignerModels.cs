using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Nodes;
using FlowPorts = XNY.Scada.Flow.Model.Ports;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>节点执行状态。</summary>
public enum NodeExecState
{
    /// <summary>无。</summary>
    None,
    /// <summary>运行。</summary>
    Running,
    /// <summary>结束。</summary>
    Done,
    /// <summary>失败。</summary>
    Failed
}

/// <summary>节点属性项。</summary>
public sealed partial class NodePropertyItem : ObservableObject
{
    /// <summary>创建节点属性项。</summary>
    public NodePropertyItem(NodePropertyDescriptor descriptor, string? value)
    {
        Descriptor = descriptor;
        _value = value ?? descriptor.Default;
    }

    /// <summary>描述。</summary>
    public NodePropertyDescriptor Descriptor { get; }
    /// <summary>节点属性键。</summary>
    public string Key => Descriptor.Key;
    /// <summary>显示名。</summary>
    public string DisplayName => Descriptor.Required ? Descriptor.DisplayName + " *" : Descriptor.DisplayName;
    /// <summary>节点属性种类。</summary>
    public NodePropertyKind Kind => Descriptor.Kind;
    /// <summary>节点属性说明。</summary>
    public string Description => Descriptor.Description;
    /// <summary>可选值。</summary>
    public IReadOnlyList<string> Choices => Descriptor.Choices;
    /// <summary>是否多行。</summary>
    public bool IsMultiline => Kind is NodePropertyKind.Assignments or NodePropertyKind.Code or NodePropertyKind.Template;
    /// <summary>是否布尔。</summary>
    public bool IsBool => Kind == NodePropertyKind.Bool;
    /// <summary>是否枚举。</summary>
    public bool IsEnum => Kind == NodePropertyKind.Enum;

    /// <summary>编辑器类型：Text / Multi / Combo / Bool，界面据此选择控件</summary>
    public string Editor => IsBool ? "Bool" : IsMultiline ? "Multi"
        : Kind is NodePropertyKind.Tag or NodePropertyKind.FlowRef or NodePropertyKind.MesRef or NodePropertyKind.Variable or NodePropertyKind.Enum ? "Combo"
        : "Text";

    /// <summary>下拉候选（点位、流程、MES 接口、变量等），由设计器填充</summary>
    [ObservableProperty] private IReadOnlyList<string> _suggestions = [];

    /// <summary>节点属性值。</summary>
    [ObservableProperty] private string _value;
}

/// <summary>端口号信息。</summary>
/// <param name="Name">名称。</param>
/// <param name="Left">左。</param>
public sealed record PortInfo(string Name, double Left);

/// <summary>流程节点页面的视图模型。</summary>
public sealed partial class FlowNodeViewModel : ObservableObject
{
    /// <summary>宽度。</summary>
    public const double Width = 170;
    /// <summary>高度。</summary>
    public const double Height = 64;

    /// <summary>创建流程节点视图模型。</summary>
    public FlowNodeViewModel(NodeDef def, NodeDescriptor? descriptor)
    {
        Id = def.Id;
        Type = def.Type;
        Descriptor = descriptor;
        _name = def.Name;
        _x = def.X;
        _y = def.Y;
        _timeoutMs = def.TimeoutMs;
        var known = descriptor?.Properties ?? [];
        foreach (var p in known)
        {
            var item = new NodePropertyItem(p, def.Properties.GetValueOrDefault(p.Key));
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(NodePropertyItem.Value)) return;
                if (p.Key == "Cases") RebuildPorts();
                OnPropertyChanged(nameof(Summary));
                Changed?.Invoke(this, EventArgs.Empty);
            };
            Properties.Add(item);
        }
        // 保留未在描述中声明的属性（插件卸载或旧版本），避免保存时丢失
        foreach (var kv in def.Properties.Where(kv => known.All(p => p.Key != kv.Key)))
        {
            ExtraProperties[kv.Key] = kv.Value;
        }
        RebuildPorts();
    }

    /// <summary>成员发生变化时触发。</summary>
    public event EventHandler? Changed;

    /// <summary>流程节点标识。</summary>
    public string Id { get; }
    /// <summary>流程节点类型。</summary>
    public string Type { get; }
    /// <summary>描述。</summary>
    public NodeDescriptor? Descriptor { get; }
    /// <summary>类型名称。</summary>
    public string TypeName => Descriptor?.DisplayName ?? Type;
    /// <summary>分类。</summary>
    public string Category => Descriptor?.Category ?? "未知";
    /// <summary>节点参数。</summary>
    public ObservableCollection<NodePropertyItem> Properties { get; } = [];
    /// <summary>特属性表。</summary>
    public Dictionary<string, string> ExtraProperties { get; } = new();
    /// <summary>输出端口。</summary>
    public ObservableCollection<string> Ports { get; } = [];

    /// <summary>流程节点名称。</summary>
    [ObservableProperty] private string _name;
    /// <summary>x。</summary>
    [ObservableProperty] private double _x;
    /// <summary>y。</summary>
    [ObservableProperty] private double _y;
    /// <summary>超时毫秒。</summary>
    [ObservableProperty] private int _timeoutMs;
    /// <summary>是否起点。</summary>
    [ObservableProperty] private bool _isStart;
    /// <summary>是否选中。</summary>
    [ObservableProperty] private bool _isSelected;
    /// <summary>是否有断点。</summary>
    [ObservableProperty] private bool _hasBreakpoint;
    /// <summary>是否有错误。</summary>
    [ObservableProperty] private bool _hasError;
    /// <summary>执行状态。</summary>
    [ObservableProperty] private NodeExecState _execState;

    /// <summary>标题。</summary>
    public string Title => string.IsNullOrWhiteSpace(Name) ? TypeName : Name;

    /// <summary>节点上显示的摘要：第一个非空属性</summary>
    public string Summary
    {
        get
        {
            var p = Properties.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Value) && p.Kind != NodePropertyKind.Code);
            if (p == null) return Id;
            var v = p.Value.ReplaceLineEndings(" ");
            return v.Length > 28 ? v[..28] + "…" : v;
        }
    }

    /// <summary>当名称变化时更新关联状态。</summary>
    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>当设计器中的横坐标变化时更新关联状态。</summary>
    partial void OnXChanged(double value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>当设计器中的纵坐标变化时更新关联状态。</summary>
    partial void OnYChanged(double value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>当超时（毫秒）变化时更新关联状态。</summary>
    partial void OnTimeoutMsChanged(int value)
    {
        RebuildPorts();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>获取属性。</summary>
    public string? GetProperty(string key) => Properties.FirstOrDefault(p => p.Key == key)?.Value ?? ExtraProperties.GetValueOrDefault(key);

    /// <summary>重新生成端口。</summary>
    private void RebuildPorts()
    {
        var ports = (Descriptor?.Ports ?? [FlowPorts.Next]).ToList();
        if (Type == "Switch")
        {
            var cases = GetProperty("Cases")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            ports.InsertRange(0, cases.Select(c => $"case:{c}"));
        }
        if (TimeoutMs > 0 && !ports.Contains(FlowPorts.Timeout)) ports.Add(FlowPorts.Timeout);
        // 所有节点都可以接 error 端口处理异常
        if (Type != "End" && !ports.Contains(FlowPorts.Error)) ports.Add(FlowPorts.Error);
        if (ports.SequenceEqual(Ports)) return;
        Ports.Clear();
        foreach (var p in ports) Ports.Add(p);
        OnPropertyChanged(nameof(PortItems));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>端口绘制位置（圆点直径 12，Left 为圆点左边距）</summary>
    public IReadOnlyList<PortInfo> PortItems => Ports.Select(p => new PortInfo(p, PortOffset(p) - 6)).ToList();

    /// <summary>端口在节点底边的横坐标（相对节点左上角）</summary>
    public double PortOffset(string port)
    {
        var idx = Ports.IndexOf(port);
        if (idx < 0) idx = Ports.Count;
        return (idx + 1) * Width / (Ports.Count + 2);
    }

    /// <summary>转换为定义。</summary>
    public NodeDef ToDef()
    {
        var props = new Dictionary<string, string>(ExtraProperties);
        foreach (var p in Properties.Where(p => !string.IsNullOrEmpty(p.Value))) props[p.Key] = p.Value;
        return new NodeDef { Id = Id, Type = Type, Name = Name, X = Math.Round(X), Y = Math.Round(Y), TimeoutMs = TimeoutMs, Properties = props };
    }
}

/// <summary>流程连线页面的视图模型。</summary>
public sealed partial class FlowEdgeViewModel : ObservableObject
{
    /// <summary>查找表。</summary>
    private readonly Func<string, FlowNodeViewModel?> _lookup;

    /// <summary>创建流程连线视图模型。</summary>
    public FlowEdgeViewModel(EdgeDef def, Func<string, FlowNodeViewModel?> lookup)
    {
        _lookup = lookup;
        _fromId = def.From;
        _port = def.Port;
        _toId = def.To;
        UpdateGeometry();
    }

    /// <summary>从标识。</summary>
    [ObservableProperty] private string _fromId;
    /// <summary>端口号。</summary>
    [ObservableProperty] private string _port;
    /// <summary>到标识。</summary>
    [ObservableProperty] private string _toId;
    /// <summary>是否选中。</summary>
    [ObservableProperty] private bool _isSelected;
    /// <summary>起点横坐标。</summary>
    [ObservableProperty] private double _x1;
    /// <summary>起点纵坐标。</summary>
    [ObservableProperty] private double _y1;
    /// <summary>终点横坐标。</summary>
    [ObservableProperty] private double _x2;
    /// <summary>终点纵坐标。</summary>
    [ObservableProperty] private double _y2;

    /// <summary>贝塞尔曲线路径数据（WPF Path.Data 可直接绑定）</summary>
    [ObservableProperty] private string _pathData = "";

    /// <summary>标签X。</summary>
    public double LabelX => (X1 + X2) / 2;
    /// <summary>标签纵坐标。</summary>
    public double LabelY => (Y1 + Y2) / 2;

    /// <summary>当从标识变化时更新关联状态。</summary>
    partial void OnFromIdChanged(string value) => UpdateGeometry();
    /// <summary>当到标识变化时更新关联状态。</summary>
    partial void OnToIdChanged(string value) => UpdateGeometry();
    /// <summary>当输出端口变化时更新关联状态。</summary>
    partial void OnPortChanged(string value) => UpdateGeometry();

    /// <summary>更新几何。</summary>
    public void UpdateGeometry()
    {
        var from = _lookup(FromId);
        var to = _lookup(ToId);
        if (from == null || to == null) return;
        X1 = from.X + from.PortOffset(Port);
        Y1 = from.Y + FlowNodeViewModel.Height;
        X2 = to.X + FlowNodeViewModel.Width / 2;
        Y2 = to.Y;
        // 回连（目标在上方）时控制点拉得更远，曲线绕开节点
        var dy = Math.Max(40, Math.Abs(Y2 - Y1) / 2);
        PathData = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"M {X1:0.#},{Y1:0.#} C {X1:0.#},{Y1 + dy:0.#} {X2:0.#},{Y2 - dy:0.#} {X2:0.#},{Y2:0.#} M {X2 - 5:0.#},{Y2 - 9:0.#} L {X2:0.#},{Y2:0.#} L {X2 + 5:0.#},{Y2 - 9:0.#}");
        OnPropertyChanged(nameof(LabelX));
        OnPropertyChanged(nameof(LabelY));
    }

    /// <summary>转换为定义。</summary>
    public EdgeDef ToDef() => new() { From = FromId, Port = Port, To = ToId };
}

/// <summary>触发器行。</summary>
public sealed partial class TriggerRow : ObservableObject
{
    /// <summary>触发器行类型。</summary>
    [ObservableProperty] private TriggerType _type = TriggerType.TagRisingEdge;
    /// <summary>标记。</summary>
    [ObservableProperty] private string? _tag;
    /// <summary>条件。</summary>
    [ObservableProperty] private string? _condition;
    /// <summary>间隔毫秒。</summary>
    [ObservableProperty] private int _intervalMs;
    /// <summary>源。</summary>
    [ObservableProperty] private string? _source;
    /// <summary>工位。</summary>
    [ObservableProperty] private string? _station;
    /// <summary>启用。</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>起点节点。</summary>
    public static TriggerRow From(TriggerDef t) => new()
    {
        Type = t.Type, Tag = t.Tag, Condition = t.Condition, IntervalMs = t.IntervalMs, Source = t.Source, Station = t.Station, Enabled = t.Enabled
    };

    /// <summary>转换为定义。</summary>
    public TriggerDef ToDef() => new()
    {
        Type = Type, Tag = Blank(Tag), Condition = Blank(Condition), IntervalMs = IntervalMs, Source = Blank(Source), Station = Blank(Station), Enabled = Enabled
    };

    /// <summary>空白。</summary>
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>变量行。</summary>
public sealed partial class VariableRow : ObservableObject
{
    /// <summary>变量行名称。</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>默认值。</summary>
    [ObservableProperty] private string? _defaultValue;
    /// <summary>范围。</summary>
    [ObservableProperty] private VariableScope _scope;
    /// <summary>描述。</summary>
    [ObservableProperty] private string? _description;

    /// <summary>起点节点。</summary>
    public static VariableRow From(VariableDef v) => new() { Name = v.Name, DefaultValue = v.DefaultValue, Scope = v.Scope, Description = v.Description };

    /// <summary>转换为定义。</summary>
    public VariableDef ToDef() => new() { Name = Name.Trim(), DefaultValue = DefaultValue, Scope = Scope, Description = Description };
}

/// <summary>工具面板组。</summary>
/// <param name="Category">分类。</param>
/// <param name="Nodes">节点。</param>
public sealed record PaletteGroup(string Category, IReadOnlyList<NodeDescriptor> Nodes);
