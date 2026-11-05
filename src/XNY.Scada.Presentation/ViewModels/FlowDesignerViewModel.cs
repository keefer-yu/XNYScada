using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Flows;
using XNY.Scada.Application.Mes;
using XNY.Scada.Device;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Nodes;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>
/// 流程设计器：WPF 以画布方式编辑（拖拽节点、端口连线），WinForms 以表格方式编辑同一模型。
/// 支持校验、保存草稿、电子签名发布、模拟运行、断点、JSON 直接编辑与撤销。
/// </summary>
public sealed partial class FlowDesignerViewModel : ViewModelBase
{
    private const int MaxUndo = 50;
    private readonly FlowDefinitionService _service;
    private readonly INodeRegistry _registry;
    private readonly IFlowEngine _engine;
    private readonly ITagService _tags;
    private readonly MesConfigService _mes;
    private readonly Dictionary<string, FlowNodeViewModel> _nodeMap = new();
    private readonly Stack<string> _undo = new();
    private bool _loading;

    public FlowDesignerViewModel(ViewModelContext context, FlowDefinitionService service, INodeRegistry registry,
        IFlowEngine engine, ITagService tags, MesConfigService mes) : base(context)
    {
        _service = service;
        _registry = registry;
        _engine = engine;
        _tags = tags;
        _mes = mes;
        Palette = registry.Descriptors.GroupBy(d => d.Category)
            .Select(g => new PaletteGroup(g.Key, g.ToList())).ToList();
        NodeTypes = registry.Descriptors;

        NewFlowCommand = Secured(Permissions.Flow.Edit, NewFlowAsync);
        OpenCommand = Command(() => OpenAsync(SelectedFlow?.Code, null), () => SelectedFlow != null);
        OpenVersionCommand = Command(() => OpenAsync(SelectedFlow?.Code, SelectedVersion?.Version), () => SelectedVersion != null);
        AddNodeCommand = Secured<NodeDescriptor>(Permissions.Flow.Edit, d =>
        {
            if (d != null) AddNode(d.Type, 40 + Nodes.Count % 5 * 30, 40 + Nodes.Count * 20);
            return Task.CompletedTask;
        });
        DeleteSelectedCommand = Secured(Permissions.Flow.Edit, () => { DeleteSelected(); return Task.CompletedTask; });
        SetStartCommand = Secured(Permissions.Flow.Edit, () =>
        {
            if (SelectedNode != null) SetStart(SelectedNode.Id);
            return Task.CompletedTask;
        });
        AddEdgeCommand = Secured(Permissions.Flow.Edit, () =>
        {
            var from = SelectedNode ?? Nodes.FirstOrDefault();
            if (from == null) return Task.CompletedTask;
            PushUndo();
            var e = CreateEdge(new EdgeDef { From = from.Id, Port = from.Ports.FirstOrDefault() ?? Ports.Next, To = Nodes.FirstOrDefault(n => n != from)?.Id ?? from.Id });
            Edges.Add(e);
            SelectedEdge = e;
            MarkDirty();
            return Task.CompletedTask;
        });
        UndoCommand = Command(() => { Undo(); return Task.CompletedTask; }, () => _undo.Count > 0);
        AutoLayoutCommand = Secured(Permissions.Flow.Edit, () => { AutoLayout(); return Task.CompletedTask; });
        ValidateCommand = Command(() => { Validate(); return Task.CompletedTask; });
        SaveCommand = Secured(Permissions.Flow.Edit, SaveAsync, () => IsLoaded);
        PublishCommand = Secured(Permissions.Flow.Publish, PublishAsync, () => IsLoaded);
        SimulateCommand = Secured(Permissions.Flow.Debug, SimulateAsync, () => IsLoaded);
        ToggleBreakpointCommand = Secured(Permissions.Flow.Debug, () =>
        {
            if (SelectedNode == null) return Task.CompletedTask;
            _engine.Debugger.ToggleBreakpoint(Code, SelectedNode.Id);
            SelectedNode.HasBreakpoint = _engine.Debugger.HasBreakpoint(Code, SelectedNode.Id);
            return Task.CompletedTask;
        });
        DeleteFlowCommand = Secured(Permissions.Flow.Publish, DeleteFlowAsync, () => SelectedFlow != null);
        ExportCommand = Command(ExportAsync, () => IsLoaded);
        ImportCommand = Secured(Permissions.Flow.Edit, ImportAsync);
        RefreshJsonCommand = Command(() => { JsonText = ToDocument().ToJson(); return Task.CompletedTask; }, () => IsLoaded);
        ApplyJsonCommand = Secured(Permissions.Flow.Edit, ApplyJsonAsync);
        AddTriggerCommand = Secured(Permissions.Flow.Edit, () => { Triggers.Add(new TriggerRow()); MarkDirty(); return Task.CompletedTask; });
        RemoveTriggerCommand = Secured<TriggerRow>(Permissions.Flow.Edit, t => { if (t != null) Triggers.Remove(t); MarkDirty(); return Task.CompletedTask; });
        AddVariableCommand = Secured(Permissions.Flow.Edit, () => { Variables.Add(new VariableRow { Name = $"Var{Variables.Count + 1}" }); MarkDirty(); return Task.CompletedTask; });
        RemoveVariableCommand = Secured<VariableRow>(Permissions.Flow.Edit, v => { if (v != null) Variables.Remove(v); MarkDirty(); return Task.CompletedTask; });
        ClearExecStateCommand = Command(() => { ClearExecState(); return Task.CompletedTask; });

        Triggers.CollectionChanged += OnRowsChanged;
        Variables.CollectionChanged += OnRowsChanged;
        Edges.CollectionChanged += (_, _) => MarkDirty();
    }

    public override string Title => "流程设计";

    public IReadOnlyList<PaletteGroup> Palette { get; }
    public IReadOnlyList<NodeDescriptor> NodeTypes { get; }
    public IReadOnlyList<TriggerType> TriggerTypes { get; } = Enum.GetValues<TriggerType>();
    public IReadOnlyList<VariableScope> VariableScopes { get; } = Enum.GetValues<VariableScope>();
    public IReadOnlyList<FlowConcurrency> ConcurrencyModes { get; } = Enum.GetValues<FlowConcurrency>();

    public ObservableCollection<FlowDefinitionEntity> Flows { get; } = [];
    public ObservableCollection<FlowVersionEntity> Versions { get; } = [];
    public ObservableCollection<FlowNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<FlowEdgeViewModel> Edges { get; } = [];
    public ObservableCollection<TriggerRow> Triggers { get; } = [];
    public ObservableCollection<VariableRow> Variables { get; } = [];
    public ObservableCollection<ValidationIssue> Issues { get; } = [];
    public ObservableCollection<NodeTrace> SimulationTrace { get; } = [];
    public ObservableCollection<KeyValuePair<string, string>> SimulationVariables { get; } = [];

    /// <summary>节点属性编辑器下拉候选</summary>
    public List<string> TagNames { get; private set; } = [];
    public List<string> FlowCodes { get; private set; } = [];
    public List<string> MesCodes { get; private set; } = [];
    public IEnumerable<string> NodeIds => Nodes.Select(n => n.Id);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(DeleteFlowCommand))]
    private FlowDefinitionEntity? _selectedFlow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenVersionCommand))]
    private FlowVersionEntity? _selectedVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(PublishCommand), nameof(SimulateCommand), nameof(ExportCommand), nameof(RefreshJsonCommand))]
    private bool _isLoaded;

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _station;
    [ObservableProperty] private FlowConcurrency _concurrency;
    [ObservableProperty] private int _timeoutMs;
    [ObservableProperty] private int _version;
    [ObservableProperty] private string _startNodeId = "";
    [ObservableProperty] private string _jsonText = "";
    [ObservableProperty] private string? _simulationInput;
    [ObservableProperty] private string? _simulationResult;
    [ObservableProperty] private FlowNodeViewModel? _selectedNode;
    [ObservableProperty] private FlowEdgeViewModel? _selectedEdge;
    [ObservableProperty] private double _canvasWidth = 1600;
    [ObservableProperty] private double _canvasHeight = 1200;

    public IAsyncRelayCommand NewFlowCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand OpenVersionCommand { get; }
    public IAsyncRelayCommand<NodeDescriptor> AddNodeCommand { get; }
    public IAsyncRelayCommand DeleteSelectedCommand { get; }
    public IAsyncRelayCommand SetStartCommand { get; }
    public IAsyncRelayCommand AddEdgeCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public IAsyncRelayCommand AutoLayoutCommand { get; }
    public IAsyncRelayCommand ValidateCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand PublishCommand { get; }
    public IAsyncRelayCommand SimulateCommand { get; }
    public IAsyncRelayCommand ToggleBreakpointCommand { get; }
    public IAsyncRelayCommand DeleteFlowCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IAsyncRelayCommand RefreshJsonCommand { get; }
    public IAsyncRelayCommand ApplyJsonCommand { get; }
    public IAsyncRelayCommand AddTriggerCommand { get; }
    public IAsyncRelayCommand<TriggerRow> RemoveTriggerCommand { get; }
    public IAsyncRelayCommand AddVariableCommand { get; }
    public IAsyncRelayCommand<VariableRow> RemoveVariableCommand { get; }
    public IAsyncRelayCommand ClearExecStateCommand { get; }

    public bool CanEdit => Can(Permissions.Flow.Edit);

    protected override async Task OnActivatedAsync()
    {
        TagNames = _tags.GetAll().Select(t => t.Tag.FullName).OrderBy(n => n).ToList();
        MesCodes = (await _mes.ListAsync()).Select(m => m.Code).ToList();
        await ReloadFlowsAsync();
        OnPropertyChanged(nameof(TagNames));
        OnPropertyChanged(nameof(MesCodes));
        _engine.NodeStarted += OnEngineNode;
        _engine.NodeCompleted += OnEngineNode;
    }

    protected override void OnDeactivated()
    {
        _engine.NodeStarted -= OnEngineNode;
        _engine.NodeCompleted -= OnEngineNode;
    }

    /// <summary>生产运行时在画布上实时高亮当前流程的执行节点</summary>
    private void OnEngineNode(object? sender, FlowNodeEventArgs e)
    {
        if (!IsLoaded || !string.Equals(e.Instance.FlowCode, Code, StringComparison.OrdinalIgnoreCase)) return;
        Ui.BeginInvoke(() =>
        {
            if (!_nodeMap.TryGetValue(e.Node.Id, out var n)) return;
            n.ExecState = e.Trace == null ? NodeExecState.Running : e.Trace.Error != null ? NodeExecState.Failed : NodeExecState.Done;
        });
    }

    private async Task ReloadFlowsAsync()
    {
        var code = SelectedFlow?.Code;
        var list = await _service.ListAsync();
        Replace(Flows, list);
        FlowCodes = list.Select(f => f.Code).ToList();
        OnPropertyChanged(nameof(FlowCodes));
        SelectedFlow = Flows.FirstOrDefault(f => f.Code == code);
    }

    partial void OnSelectedFlowChanged(FlowDefinitionEntity? value) => _ = RunAsync(async () =>
    {
        Replace(Versions, value == null ? [] : await _service.GetVersionsAsync(value.Code));
    });

    partial void OnSelectedNodeChanged(FlowNodeViewModel? oldValue, FlowNodeViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null)
        {
            newValue.IsSelected = true;
            SelectedEdge = null;
            FillSuggestions(newValue);
        }
    }

    partial void OnSelectedEdgeChanged(FlowEdgeViewModel? oldValue, FlowEdgeViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null)
        {
            newValue.IsSelected = true;
            SelectedNode = null;
        }
    }

    partial void OnCodeChanged(string value) => MarkDirty();
    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnDescriptionChanged(string? value) => MarkDirty();
    partial void OnStationChanged(string? value) => MarkDirty();
    partial void OnConcurrencyChanged(FlowConcurrency value) => MarkDirty();
    partial void OnTimeoutMsChanged(int value) => MarkDirty();

    private void FillSuggestions(FlowNodeViewModel node)
    {
        var vars = Variables.Select(v => v.Name).ToList();
        foreach (var p in node.Properties)
        {
            p.Suggestions = p.Kind switch
            {
                NodePropertyKind.Tag => TagNames,
                NodePropertyKind.FlowRef => FlowCodes,
                NodePropertyKind.MesRef => MesCodes,
                NodePropertyKind.Variable => vars,
                NodePropertyKind.Enum => p.Choices,
                _ => []
            };
        }
    }

    private void OnRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<INotifyPropertyChanged>() ?? [])
        {
            item.PropertyChanged += (_, _) => MarkDirty();
        }
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (_loading || !IsLoaded) return;
        IsDirty = true;
        UndoCommand.NotifyCanExecuteChanged();
    }

    // ---------------- 文档加载 / 生成 ----------------

    private void LoadDocument(FlowDocument doc)
    {
        _loading = true;
        try
        {
            SelectedNode = null;
            SelectedEdge = null;
            foreach (var n in Nodes) n.Changed -= OnNodeChanged;
            Nodes.Clear();
            Edges.Clear();
            _nodeMap.Clear();
            Code = doc.Code;
            Name = doc.Name;
            Description = doc.Description;
            Station = doc.Station;
            Concurrency = doc.Concurrency;
            TimeoutMs = doc.TimeoutMs;
            Version = doc.Version;
            foreach (var n in doc.Nodes) AddNodeVm(n);
            foreach (var e in doc.Edges) Edges.Add(CreateEdge(e));
            SetStart(doc.StartNodeId);
            Replace(Triggers, doc.Triggers.Select(TriggerRow.From));
            Replace(Variables, doc.Variables.Select(VariableRow.From));
            Issues.Clear();
            ClearExecState();
            UpdateCanvasSize();
            JsonText = doc.ToJson();
            IsLoaded = true;
            IsDirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    public FlowDocument ToDocument() => new()
    {
        Code = Code.Trim(),
        Name = Name.Trim(),
        Description = Description,
        Station = string.IsNullOrWhiteSpace(Station) ? null : Station,
        Concurrency = Concurrency,
        TimeoutMs = TimeoutMs,
        Version = Version,
        StartNodeId = StartNodeId,
        Triggers = Triggers.Select(t => t.ToDef()).ToList(),
        Variables = Variables.Where(v => !string.IsNullOrWhiteSpace(v.Name)).Select(v => v.ToDef()).ToList(),
        Nodes = Nodes.Select(n => n.ToDef()).ToList(),
        Edges = Edges.Where(e => _nodeMap.ContainsKey(e.FromId) && _nodeMap.ContainsKey(e.ToId)).Select(e => e.ToDef()).ToList()
    };

    private FlowNodeViewModel AddNodeVm(NodeDef def)
    {
        var vm = new FlowNodeViewModel(def, _registry.Find(def.Type))
        {
            HasBreakpoint = _engine.Debugger.HasBreakpoint(Code, def.Id)
        };
        vm.Changed += OnNodeChanged;
        _nodeMap[vm.Id] = vm;
        Nodes.Add(vm);
        OnPropertyChanged(nameof(NodeIds));
        return vm;
    }

    private void OnNodeChanged(object? sender, EventArgs e)
    {
        if (sender is not FlowNodeViewModel n) return;
        foreach (var edge in Edges.Where(x => x.FromId == n.Id || x.ToId == n.Id)) edge.UpdateGeometry();
        MarkDirty();
    }

    private FlowEdgeViewModel CreateEdge(EdgeDef def)
    {
        var e = new FlowEdgeViewModel(def, id => _nodeMap.GetValueOrDefault(id));
        e.PropertyChanged += OnEdgePropertyChanged;
        return e;
    }

    private void OnEdgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlowEdgeViewModel.FromId) or nameof(FlowEdgeViewModel.ToId) or nameof(FlowEdgeViewModel.Port)) MarkDirty();
    }

    private void SetStart(string? id)
    {
        if (string.IsNullOrEmpty(id) || !_nodeMap.ContainsKey(id)) id = Nodes.FirstOrDefault(n => n.Type == "Start")?.Id ?? Nodes.FirstOrDefault()?.Id ?? "";
        StartNodeId = id;
        foreach (var n in Nodes) n.IsStart = n.Id == id;
        MarkDirty();
    }

    private void UpdateCanvasSize()
    {
        CanvasWidth = Math.Max(1600, Nodes.Count == 0 ? 0 : Nodes.Max(n => n.X) + FlowNodeViewModel.Width + 200);
        CanvasHeight = Math.Max(1200, Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Y) + FlowNodeViewModel.Height + 200);
    }

    // ---------------- 编辑操作（画布调用） ----------------

    public FlowNodeViewModel? AddNode(string type, double x, double y)
    {
        if (!CanEdit || !IsLoaded) return null;
        var d = _registry.Find(type);
        if (d == null) return null;
        if (d.RequiredPermission != null && !Can(d.RequiredPermission))
        {
            Dialogs.ShowError($"当前用户无权使用节点「{d.DisplayName}」");
            return null;
        }
        PushUndo();
        var prefix = new string(type.Where(char.IsUpper).ToArray()).ToLowerInvariant();
        if (prefix.Length == 0) prefix = "n";
        var i = 1;
        while (_nodeMap.ContainsKey($"{prefix}{i}")) i++;
        var def = new NodeDef
        {
            Id = $"{prefix}{i}", Type = type, Name = d.DisplayName, X = Math.Max(0, Math.Round(x / 10) * 10), Y = Math.Max(0, Math.Round(y / 10) * 10),
            Properties = d.Properties.Where(p => !string.IsNullOrEmpty(p.Default)).ToDictionary(p => p.Key, p => p.Default)
        };
        var vm = AddNodeVm(def);
        if (Nodes.Count == 1) SetStart(vm.Id);
        SelectedNode = vm;
        UpdateCanvasSize();
        MarkDirty();
        return vm;
    }

    /// <summary>画布拖动结束时调用，用于撤销记录与画布扩展</summary>
    public void BeginMove() => PushUndo();

    public void EndMove() => UpdateCanvasSize();

    public void MoveNode(FlowNodeViewModel node, double x, double y)
    {
        if (!CanEdit) return;
        node.X = Math.Max(0, Math.Round(x / 10) * 10);
        node.Y = Math.Max(0, Math.Round(y / 10) * 10);
    }

    /// <summary>从节点端口连线到目标节点；同一端口已有连线时替换</summary>
    public void Connect(FlowNodeViewModel from, string port, FlowNodeViewModel to)
    {
        if (!CanEdit || from == to && port != Ports.Body) return;
        PushUndo();
        foreach (var old in Edges.Where(e => e.FromId == from.Id && string.Equals(e.Port, port, StringComparison.OrdinalIgnoreCase)).ToList()) Edges.Remove(old);
        var edge = CreateEdge(new EdgeDef { From = from.Id, Port = port, To = to.Id });
        Edges.Add(edge);
        SelectedEdge = edge;
    }

    public void DeleteSelected()
    {
        if (!CanEdit) return;
        if (SelectedEdge != null)
        {
            PushUndo();
            Edges.Remove(SelectedEdge);
            SelectedEdge = null;
            return;
        }
        if (SelectedNode == null) return;
        PushUndo();
        var n = SelectedNode;
        SelectedNode = null;
        foreach (var e in Edges.Where(e => e.FromId == n.Id || e.ToId == n.Id).ToList()) Edges.Remove(e);
        n.Changed -= OnNodeChanged;
        Nodes.Remove(n);
        _nodeMap.Remove(n.Id);
        OnPropertyChanged(nameof(NodeIds));
        if (StartNodeId == n.Id) SetStart(null);
        MarkDirty();
    }

    private void PushUndo()
    {
        if (!IsLoaded) return;
        _undo.Push(ToDocument().ToJson());
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var k in keep) _undo.Push(k);
        }
        UndoCommand.NotifyCanExecuteChanged();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        LoadDocument(FlowDocument.FromJson(_undo.Pop()));
        IsDirty = true;
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>按连线做分层布局（BFS 深度为行，同层依次排列）</summary>
    private void AutoLayout()
    {
        if (Nodes.Count == 0) return;
        PushUndo();
        var depth = new Dictionary<string, int>();
        var queue = new Queue<string>();
        if (_nodeMap.ContainsKey(StartNodeId))
        {
            depth[StartNodeId] = 0;
            queue.Enqueue(StartNodeId);
        }
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var e in Edges.Where(e => e.FromId == id))
            {
                if (depth.ContainsKey(e.ToId) || !_nodeMap.ContainsKey(e.ToId)) continue;
                depth[e.ToId] = depth[id] + 1;
                queue.Enqueue(e.ToId);
            }
        }
        var maxDepth = depth.Count == 0 ? 0 : depth.Values.Max() + 1;
        foreach (var n in Nodes.Where(n => !depth.ContainsKey(n.Id))) depth[n.Id] = maxDepth;
        foreach (var layer in Nodes.GroupBy(n => depth[n.Id]))
        {
            var col = 0;
            foreach (var n in layer)
            {
                n.X = 60 + col++ * (FlowNodeViewModel.Width + 60);
                n.Y = 40 + layer.Key * (FlowNodeViewModel.Height + 70);
            }
        }
        UpdateCanvasSize();
    }

    private void ClearExecState()
    {
        foreach (var n in Nodes) n.ExecState = NodeExecState.None;
    }

    // ---------------- 命令实现 ----------------

    private async Task<bool> ConfirmDiscardAsync()
    {
        await Task.CompletedTask;
        return !IsDirty || Dialogs.Confirm("当前流程有未保存的修改，确定放弃？");
    }

    private async Task NewFlowAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var code = Dialogs.Prompt("请输入流程编码（字母数字，唯一）", "新建流程", "NewFlow");
        if (string.IsNullOrWhiteSpace(code)) return;
        if (Flows.Any(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException($"流程 {code} 已存在");
        _undo.Clear();
        LoadDocument(new FlowDocument
        {
            Code = code.Trim(), Name = code.Trim(), StartNodeId = "s1",
            Nodes = [new NodeDef { Id = "s1", Type = "Start", Name = "开始", X = 300, Y = 40 }],
            Triggers = [new TriggerDef { Type = TriggerType.Manual }]
        });
        IsDirty = true;
    }

    private async Task OpenAsync(string? code, int? version)
    {
        if (code == null || !await ConfirmDiscardAsync()) return;
        var doc = version == null ? await _service.GetLatestAsync(code) : await _service.GetVersionAsync(code, version.Value);
        if (doc == null) throw new InvalidOperationException($"流程 {code} 没有版本数据");
        _undo.Clear();
        LoadDocument(doc);
        StatusMessage = $"已打开 {code} v{doc.Version}";
    }

    private bool Validate()
    {
        var issues = _service.Validate(ToDocument());
        Replace(Issues, issues);
        foreach (var n in Nodes) n.HasError = issues.Any(i => i.IsError && i.NodeId == n.Id);
        StatusMessage = issues.Count == 0 ? "校验通过" : $"错误 {issues.Count(i => i.IsError)} 个，警告 {issues.Count(i => !i.IsError)} 个";
        return issues.All(i => !i.IsError);
    }

    private async Task SaveAsync()
    {
        Validate();
        var remark = Dialogs.Prompt("修改说明（将记录到版本历史）", "保存草稿", "");
        if (remark == null) return;
        var doc = ToDocument();
        Version = await _service.SaveDraftAsync(doc, remark);
        IsDirty = false;
        JsonText = doc.ToJson();
        await ReloadFlowsAsync();
        SelectedFlow = Flows.FirstOrDefault(f => f.Code == Code);
        StatusMessage = $"已保存草稿 v{Version}";
    }

    private async Task PublishAsync()
    {
        if (!Validate())
        {
            Dialogs.ShowError("流程校验未通过，请先修正错误");
            return;
        }
        if (IsDirty) await SaveAsync();
        if (IsDirty) return;
        var signer = await SignAsync(Permissions.Flow.Publish, $"发布流程 {Code} v{Version}");
        if (signer == null) return;
        await _service.PublishAsync(Code, Version, signer, null);
        await ReloadFlowsAsync();
        SelectedFlow = Flows.FirstOrDefault(f => f.Code == Code);
        StatusMessage = _engine.IsRunning ? $"已发布 v{Version}，流程空闲后自动切换" : $"已发布 v{Version}";
    }

    private async Task SimulateAsync()
    {
        if (!Validate() && !Dialogs.Confirm("流程存在校验错误，仍要模拟运行？")) return;
        if (!Dialogs.Confirm("模拟运行会真实读写点位（模拟设备除外），确认设备处于安全状态？")) return;
        ClearExecState();
        SimulationTrace.Clear();
        SimulationVariables.Clear();
        var input = string.IsNullOrWhiteSpace(SimulationInput) ? null : Json.ToDictionary(SimulationInput);
        var instance = await _engine.RunDocumentAsync(ToDocument(), input);
        foreach (var t in instance.Trace)
        {
            SimulationTrace.Add(t);
            if (_nodeMap.TryGetValue(t.NodeId, out var n)) n.ExecState = t.Error != null ? NodeExecState.Failed : NodeExecState.Done;
        }
        foreach (var kv in instance.Variables.OrderBy(k => k.Key))
        {
            SimulationVariables.Add(new(kv.Key, Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null"));
        }
        SimulationResult = $"{instance.Status}  耗时 {instance.DurationMs}ms  {instance.Error}";
    }

    private async Task DeleteFlowAsync()
    {
        if (SelectedFlow == null || !Dialogs.Confirm($"删除流程 {SelectedFlow.Code} 及其全部版本？此操作不可恢复。")) return;
        var signer = await SignAsync(Permissions.Flow.Publish, $"删除流程 {SelectedFlow.Code}");
        if (signer == null) return;
        var code = SelectedFlow.Code;
        await _service.DeleteAsync(code);
        if (Code == code)
        {
            IsLoaded = false;
            Nodes.Clear();
            Edges.Clear();
            _nodeMap.Clear();
        }
        await ReloadFlowsAsync();
    }

    private async Task ExportAsync()
    {
        var path = Dialogs.SaveFile("流程文件|*.json", $"{Code}_v{Version}.json");
        if (path == null) return;
        await File.WriteAllTextAsync(path, ToDocument().ToJson());
        StatusMessage = $"已导出到 {path}";
    }

    private async Task ImportAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = Dialogs.OpenFile("流程文件|*.json");
        if (path == null) return;
        var doc = FlowDocument.FromJson(await File.ReadAllTextAsync(path));
        doc.Version = 0;
        _undo.Clear();
        LoadDocument(doc);
        IsDirty = true;
        StatusMessage = "已导入，保存后生效";
    }

    private Task ApplyJsonAsync()
    {
        var doc = FlowDocument.FromJson(JsonText);
        if (IsLoaded) PushUndo();
        var version = Version;
        LoadDocument(doc);
        Version = version;
        IsDirty = true;
        Validate();
        return Task.CompletedTask;
    }
}
