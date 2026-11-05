using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Nodes;
using FlowPorts = XNY.Scada.Flow.Model.Ports;

namespace XNY.Scada.Presentation.ViewModels;

public enum NodeExecState
{
    None,
    Running,
    Done,
    Failed
}

public sealed partial class NodePropertyItem : ObservableObject
{
    public NodePropertyItem(NodePropertyDescriptor descriptor, string? value)
    {
        Descriptor = descriptor;
        _value = value ?? descriptor.Default;
    }

    public NodePropertyDescriptor Descriptor { get; }
    public string Key => Descriptor.Key;
    public string DisplayName => Descriptor.Required ? Descriptor.DisplayName + " *" : Descriptor.DisplayName;
    public NodePropertyKind Kind => Descriptor.Kind;
    public string Description => Descriptor.Description;
    public IReadOnlyList<string> Choices => Descriptor.Choices;
    public bool IsMultiline => Kind is NodePropertyKind.Assignments or NodePropertyKind.Code or NodePropertyKind.Template;
    public bool IsBool => Kind == NodePropertyKind.Bool;
    public bool IsEnum => Kind == NodePropertyKind.Enum;

    /// <summary>编辑器类型：Text / Multi / Combo / Bool，界面据此选择控件</summary>
    public string Editor => IsBool ? "Bool" : IsMultiline ? "Multi"
        : Kind is NodePropertyKind.Tag or NodePropertyKind.FlowRef or NodePropertyKind.MesRef or NodePropertyKind.Variable or NodePropertyKind.Enum ? "Combo"
        : "Text";

    /// <summary>下拉候选（点位、流程、MES 接口、变量等），由设计器填充</summary>
    [ObservableProperty] private IReadOnlyList<string> _suggestions = [];

    [ObservableProperty] private string _value;
}

public sealed record PortInfo(string Name, double Left);

public sealed partial class FlowNodeViewModel : ObservableObject
{
    public const double Width = 170;
    public const double Height = 64;

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

    public event EventHandler? Changed;

    public string Id { get; }
    public string Type { get; }
    public NodeDescriptor? Descriptor { get; }
    public string TypeName => Descriptor?.DisplayName ?? Type;
    public string Category => Descriptor?.Category ?? "未知";
    public ObservableCollection<NodePropertyItem> Properties { get; } = [];
    public Dictionary<string, string> ExtraProperties { get; } = new();
    public ObservableCollection<string> Ports { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private int _timeoutMs;
    [ObservableProperty] private bool _isStart;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _hasBreakpoint;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private NodeExecState _execState;

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

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnXChanged(double value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnYChanged(double value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnTimeoutMsChanged(int value)
    {
        RebuildPorts();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? GetProperty(string key) => Properties.FirstOrDefault(p => p.Key == key)?.Value ?? ExtraProperties.GetValueOrDefault(key);

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

    public NodeDef ToDef()
    {
        var props = new Dictionary<string, string>(ExtraProperties);
        foreach (var p in Properties.Where(p => !string.IsNullOrEmpty(p.Value))) props[p.Key] = p.Value;
        return new NodeDef { Id = Id, Type = Type, Name = Name, X = Math.Round(X), Y = Math.Round(Y), TimeoutMs = TimeoutMs, Properties = props };
    }
}

public sealed partial class FlowEdgeViewModel : ObservableObject
{
    private readonly Func<string, FlowNodeViewModel?> _lookup;

    public FlowEdgeViewModel(EdgeDef def, Func<string, FlowNodeViewModel?> lookup)
    {
        _lookup = lookup;
        _fromId = def.From;
        _port = def.Port;
        _toId = def.To;
        UpdateGeometry();
    }

    [ObservableProperty] private string _fromId;
    [ObservableProperty] private string _port;
    [ObservableProperty] private string _toId;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private double _x1;
    [ObservableProperty] private double _y1;
    [ObservableProperty] private double _x2;
    [ObservableProperty] private double _y2;

    /// <summary>贝塞尔曲线路径数据（WPF Path.Data 可直接绑定）</summary>
    [ObservableProperty] private string _pathData = "";

    public double LabelX => (X1 + X2) / 2;
    public double LabelY => (Y1 + Y2) / 2;

    partial void OnFromIdChanged(string value) => UpdateGeometry();
    partial void OnToIdChanged(string value) => UpdateGeometry();
    partial void OnPortChanged(string value) => UpdateGeometry();

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

    public EdgeDef ToDef() => new() { From = FromId, Port = Port, To = ToId };
}

public sealed partial class TriggerRow : ObservableObject
{
    [ObservableProperty] private TriggerType _type = TriggerType.TagRisingEdge;
    [ObservableProperty] private string? _tag;
    [ObservableProperty] private string? _condition;
    [ObservableProperty] private int _intervalMs;
    [ObservableProperty] private string? _source;
    [ObservableProperty] private string? _station;
    [ObservableProperty] private bool _enabled = true;

    public static TriggerRow From(TriggerDef t) => new()
    {
        Type = t.Type, Tag = t.Tag, Condition = t.Condition, IntervalMs = t.IntervalMs, Source = t.Source, Station = t.Station, Enabled = t.Enabled
    };

    public TriggerDef ToDef() => new()
    {
        Type = Type, Tag = Blank(Tag), Condition = Blank(Condition), IntervalMs = IntervalMs, Source = Blank(Source), Station = Blank(Station), Enabled = Enabled
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed partial class VariableRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _defaultValue;
    [ObservableProperty] private VariableScope _scope;
    [ObservableProperty] private string? _description;

    public static VariableRow From(VariableDef v) => new() { Name = v.Name, DefaultValue = v.DefaultValue, Scope = v.Scope, Description = v.Description };

    public VariableDef ToDef() => new() { Name = Name.Trim(), DefaultValue = DefaultValue, Scope = Scope, Description = Description };
}

public sealed record PaletteGroup(string Category, IReadOnlyList<NodeDescriptor> Nodes);
