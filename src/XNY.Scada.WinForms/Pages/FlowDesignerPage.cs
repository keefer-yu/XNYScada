using System.Collections;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Nodes;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

/// <summary>
/// WinForms 流程设计器：以表格编辑节点/连线/触发器/变量，右侧为节点属性面板；
/// 「流程图」页签提供图形预览，可点选、拖动节点、从端口拖到目标节点建立连线
/// </summary>
public sealed class FlowDesignerPage : UserControl
{
    /// <summary>视图模型。</summary>
    private readonly FlowDesignerViewModel _vm;
    /// <summary>节点到添加。</summary>
    private NodeDescriptor? _nodeToAdd;

    /// <summary>创建流程设计器页面。</summary>
    public FlowDesignerPage(FlowDesignerViewModel vm)
    {
        _vm = vm;

        var dirty = Ui.Lbl("● 未保存", color: Ui.Warn);
        Bind.Visible(dirty, vm, nameof(vm.IsDirty));
        var codeLabel = Ui.Lbl("", bold: true);
        void UpdateCode() => codeLabel.Text = vm.IsLoaded ? $"{vm.Code} v{vm.Version}" : "（未打开流程）";
        Bind.Observe(vm, nameof(vm.Code), UpdateCode);
        Bind.Observe(vm, nameof(vm.Version), UpdateCode);
        Bind.Observe(vm, nameof(vm.IsLoaded), UpdateCode);

        var toolbar = Ui.Stack(
            Ui.Bar(
                Ui.Lbl("流程"),
                Ui.Combo(vm.Flows, vm, nameof(vm.SelectedFlow), 160, f => $"{((FlowDefinitionEntity)f).Code}  {((FlowDefinitionEntity)f).Name}"),
                Ui.Btn("打开", vm.OpenCommand),
                Ui.Lbl("版本"),
                Ui.Combo(vm.Versions, vm, nameof(vm.SelectedVersion), 200, v =>
                {
                    var x = (FlowVersionEntity)v;
                    return $"v{x.Version} {x.Status} {x.Remark}";
                }),
                Ui.Btn("打开版本", vm.OpenVersionCommand),
                Ui.Btn("新建", vm.NewFlowCommand),
                Ui.Btn("导入", vm.ImportCommand),
                Ui.Btn("导出", vm.ExportCommand),
                Ui.Btn("删除流程", vm.DeleteFlowCommand, ButtonKind.Danger)),
            Ui.Bar(
                codeLabel, dirty,
                Ui.Btn("保存草稿", vm.SaveCommand, ButtonKind.Primary),
                Ui.Btn("校验", vm.ValidateCommand),
                Ui.Btn("发布（签名）", vm.PublishCommand),
                Ui.Btn("模拟运行", vm.SimulateCommand),
                Ui.Btn("撤销", vm.UndoCommand),
                Ui.Btn("自动布局", vm.AutoLayoutCommand),
                Ui.Btn("清除高亮", vm.ClearExecStateCommand)));

        var tabs = Ui.Tabs(
            ("流程图", BuildCanvasTab()),
            ("节点", BuildNodesTab()),
            ("连线", BuildEdgesTab()),
            ("流程属性", BuildFlowTab()),
            ("校验", BuildIssuesTab()),
            ("模拟", BuildSimulationTab()),
            ("JSON", Ui.Rows(
                ("auto", Ui.Bar(Ui.Btn("从画布生成", vm.RefreshJsonCommand), Ui.Btn("应用到画布", vm.ApplyJsonCommand, ButtonKind.Primary))),
                ("*", Ui.Code(vm, nameof(vm.JsonText))))));

        Controls.Add(Ui.Rows(
            ("auto", Ui.Card("", toolbar)),
            ("*", Ui.Cols(("*", tabs), ("380", Ui.Card("节点属性", BuildPropertyPanel()))))));

        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) Bind.Execute(vm.SaveCommand);
            if (e.Control && e.KeyCode == Keys.Z) Bind.Execute(vm.UndoCommand);
        };
    }

    /// <summary>节点类型选择器。</summary>
    private Control NodeTypePicker()
    {
        var types = Ui.Combo(_vm.NodeTypes, this.AsNotifier(), nameof(NodeToAdd), 200, d =>
        {
            var n = (NodeDescriptor)d;
            return $"{n.Category} / {n.DisplayName}";
        });
        return types;
    }

    /// <summary>页面自身作为「待添加节点类型」的绑定源</summary>
    public NodeDescriptor? NodeToAdd
    {
        get => _nodeToAdd;
        set
        {
            _nodeToAdd = value;
            _notifier?.Raise(nameof(NodeToAdd));
        }
    }

    /// <summary>通知器。</summary>
    private SelfNotifier? _notifier;

    /// <summary>尝试转换通知器。</summary>
    private SelfNotifier AsNotifier() => _notifier ??= new SelfNotifier(this);

    /// <summary>自身通知器。</summary>
    /// <param name="page">页面。</param>
    private sealed class SelfNotifier(FlowDesignerPage page) : INotifyPropertyChanged
    {
        /// <summary>节点到添加。</summary>
        public NodeDescriptor? NodeToAdd
        {
            get => page.NodeToAdd;
            set => page.NodeToAdd = value;
        }

        /// <summary>属性发生变化时触发。</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>引发。</summary>
        public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>添加选中节点类型。</summary>
    private void AddSelectedNodeType(double x, double y)
    {
        if (NodeToAdd == null)
        {
            MessageBox.Show(this, "请先在下拉框选择节点类型", "提示");
            return;
        }
        if (_vm.AddNode(NodeToAdd.Type, x, y) is { } node) _vm.SelectedNode = node;
    }

    // ---------------- 流程图 ----------------

    /// <summary>构建画布Tab。</summary>
    private Control BuildCanvasTab()
    {
        var canvas = new FlowCanvas(_vm);
        var add = new Button { Text = "添加到画布", AutoSize = true, Margin = new Padding(0, 2, 6, 2), FlatStyle = FlatStyle.Flat };
        add.Click += (_, _) =>
        {
            var p = canvas.ViewCenter();
            AddSelectedNodeType(p.X, p.Y);
        };
        var zoom = new TrackBar { Minimum = 4, Maximum = 16, Value = 10, Width = 140, TickStyle = TickStyle.None, Margin = new Padding(0, 4, 6, 0) };
        zoom.ValueChanged += (_, _) => canvas.Zoom = zoom.Value / 10f;
        return Ui.Rows(
            ("auto", Ui.Bar(
                NodeTypePicker(), add,
                Ui.Btn("删除选中", _vm.DeleteSelectedCommand),
                Ui.Btn("设为起点", _vm.SetStartCommand),
                Ui.Btn("断点", _vm.ToggleBreakpointCommand),
                Ui.Lbl("缩放"), zoom,
                Ui.Lbl("拖动节点移动；从节点底部蓝色端口拖到目标节点建立连线；Del 删除", color: Ui.Muted))),
            ("*", canvas));
    }

    // ---------------- 节点表 ----------------

    /// <summary>构建节点列表Tab。</summary>
    private Control BuildNodesTab()
    {
        var nodes = new BoundGrid<FlowNodeViewModel>(_vm.Nodes, true,
                new Col("起点", nameof(FlowNodeViewModel.IsStart), 45),
                new Col("ID", nameof(FlowNodeViewModel.Id), 80),
                new Col("类型", nameof(FlowNodeViewModel.TypeName), 110),
                Col.Edit("名称", nameof(FlowNodeViewModel.Name), 150),
                Col.Fill("摘要", nameof(FlowNodeViewModel.Summary)),
                Col.Edit("超时ms", nameof(FlowNodeViewModel.TimeoutMs), 70),
                new Col("断点", nameof(FlowNodeViewModel.HasBreakpoint), 45),
                new Col("X", nameof(FlowNodeViewModel.X), 50),
                new Col("Y", nameof(FlowNodeViewModel.Y), 50))
            .RowColor(n => n.HasError || n.ExecState == NodeExecState.Failed ? Ui.Danger
                : n.ExecState == NodeExecState.Running ? Ui.Primary
                : n.ExecState == NodeExecState.Done ? Ui.Good : null)
            .Selected(_vm, nameof(_vm.SelectedNode));
        nodes.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && !nodes.IsCurrentCellInEditMode) Bind.Execute(_vm.DeleteSelectedCommand);
        };
        var add = new Button { Text = "添加节点", AutoSize = true, Margin = new Padding(0, 2, 6, 2), FlatStyle = FlatStyle.Flat };
        add.Click += (_, _) => AddSelectedNodeType(40 + _vm.Nodes.Count % 5 * 30, 40 + _vm.Nodes.Count * 90);
        return Ui.Rows(
            ("auto", Ui.Bar(
                NodeTypePicker(), add,
                Ui.Btn("删除选中", _vm.DeleteSelectedCommand),
                Ui.Btn("设为起点", _vm.SetStartCommand),
                Ui.Btn("断点", _vm.ToggleBreakpointCommand))),
            ("*", nodes));
    }

    // ---------------- 连线表 ----------------

    /// <summary>构建连线列表Tab。</summary>
    private Control BuildEdgesTab()
    {
        var edges = new BoundGrid<FlowEdgeViewModel>(_vm.Edges, false,
                Col.Fill("起点", nameof(FlowEdgeViewModel.FromId)),
                new Col("端口", nameof(FlowEdgeViewModel.Port), 110),
                Col.Fill("终点", nameof(FlowEdgeViewModel.ToId)))
            .Selected(_vm, nameof(_vm.SelectedEdge));
        var editor = new Panel { Dock = DockStyle.Fill };
        var bindings = new List<IDisposable>();

        void Rebuild()
        {
            bindings.ForEach(b => b.Dispose());
            bindings.Clear();
            foreach (Control c in editor.Controls) c.Dispose();
            editor.Controls.Clear();
            if (_vm.SelectedEdge is not { } edge) return;

            var from = new ComboBox { Width = 220 };
            bindings.Add(Bind.Combo(from, _vm.Nodes, edge, nameof(FlowEdgeViewModel.FromId), NodeText, n => ((FlowNodeViewModel)n).Id));
            var to = new ComboBox { Width = 220 };
            bindings.Add(Bind.Combo(to, _vm.Nodes, edge, nameof(FlowEdgeViewModel.ToId), NodeText, n => ((FlowNodeViewModel)n).Id));
            var ports = _vm.Nodes.FirstOrDefault(n => n.Id == edge.FromId)?.Ports.ToList() ?? [];
            if (!ports.Contains(edge.Port)) ports.Add(edge.Port);
            var port = new ComboBox { Width = 160 };
            bindings.Add(Bind.Combo(port, ports, edge, nameof(FlowEdgeViewModel.Port)));
            bindings.Add(Bind.Observe(edge, nameof(FlowEdgeViewModel.FromId), () => BeginInvoke(Rebuild), invokeNow: false));
            editor.Controls.Add(Ui.Form(80, ("起点", from), ("端口", port), ("终点", to)));
        }

        Bind.Observe(_vm, nameof(_vm.SelectedEdge), Rebuild);
        return Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Btn("添加连线", _vm.AddEdgeCommand),
                Ui.Btn("删除选中", _vm.DeleteSelectedCommand),
                Ui.Lbl("端口：next 默认出口、true/false 条件分支、case:X 分支、timeout 超时、error 异常", color: Ui.Muted))),
            ("*", edges),
            ("110", Ui.Card("编辑选中连线", editor)));
    }

    /// <summary>节点文本。</summary>
    private static string NodeText(object n)
    {
        var node = (FlowNodeViewModel)n;
        return $"{node.Id}  {node.Title}";
    }

    // ---------------- 流程属性 / 触发器 / 变量 ----------------

    /// <summary>构建流Tab。</summary>
    private Control BuildFlowTab()
    {
        var form = Ui.Form(110,
            ("编码", Ui.Text(_vm, nameof(_vm.Code))),
            ("名称", Ui.Text(_vm, nameof(_vm.Name))),
            ("说明", Ui.Text(_vm, nameof(_vm.Description))),
            ("工位", Ui.Text(_vm, nameof(_vm.Station))),
            ("并发策略", Ui.Combo((IEnumerable)_vm.ConcurrencyModes, _vm, nameof(_vm.Concurrency))),
            ("流程超时 ms", Ui.Text(_vm, nameof(_vm.TimeoutMs))));
        var triggers = new BoundGrid<TriggerRow>(_vm.Triggers, true,
            Col.Choice("类型", nameof(TriggerRow.Type), (IList)_vm.TriggerTypes, 130),
            Col.Fill("点位", nameof(TriggerRow.Tag)) with { Editable = true },
            Col.Edit("周期ms", nameof(TriggerRow.IntervalMs), 60),
            Col.Edit("来源", nameof(TriggerRow.Source), 90),
            Col.Edit("条件", nameof(TriggerRow.Condition), 120),
            Col.Edit("工位", nameof(TriggerRow.Station), 60),
            Col.Edit("启用", nameof(TriggerRow.Enabled), 45),
            Col.Button("删", _vm.RemoveTriggerCommand, 40));
        var variables = new BoundGrid<VariableRow>(_vm.Variables, true,
            Col.Edit("名称", nameof(VariableRow.Name), 120),
            Col.Edit("默认值", nameof(VariableRow.DefaultValue), 100),
            Col.Choice("作用域", nameof(VariableRow.Scope), (IList)_vm.VariableScopes, 90),
            Col.Fill("说明", nameof(VariableRow.Description)) with { Editable = true },
            Col.Button("删", _vm.RemoveVariableCommand, 40));
        return Ui.Rows(
            ("auto", form),
            ("*", Ui.Card("触发器（点位上升沿/下降沿/变化、定时、手动、MES 回调、流程完成）",
                Ui.Rows(("auto", Ui.Bar(Ui.Btn("添加触发器", _vm.AddTriggerCommand))), ("*", triggers)))),
            ("*", Ui.Card("变量（Instance 实例内 / Station 工位共享 / Global 全局）",
                Ui.Rows(("auto", Ui.Bar(Ui.Btn("添加变量", _vm.AddVariableCommand))), ("*", variables)))));
    }

    /// <summary>构建问题Tab。</summary>
    private Control BuildIssuesTab()
    {
        var issues = new BoundGrid<ValidationIssue>(_vm.Issues, false,
                new Col("级别", nameof(ValidationIssue.IsError), 50),
                new Col("节点", nameof(ValidationIssue.NodeId), 90),
                Col.Fill("说明", nameof(ValidationIssue.Message)))
            .RowColor(i => i.IsError ? Ui.Danger : Ui.Warn);
        issues.CellDoubleClick += (_, e) =>
        {
            if (issues.Current?.NodeId is { } id && _vm.Nodes.FirstOrDefault(n => n.Id == id) is { } node) _vm.SelectedNode = node;
        };
        return Ui.Rows(
            ("auto", Ui.Bar(Ui.Btn("重新校验", _vm.ValidateCommand), Ui.Lbl("双击定位到节点；勾选=错误（阻止发布），未勾选=警告", color: Ui.Muted))),
            ("*", issues));
    }

    /// <summary>构建模拟Tab。</summary>
    private Control BuildSimulationTab()
    {
        var trace = new BoundGrid<NodeTrace>(_vm.SimulationTrace, false,
                Col.Fill("节点", nameof(NodeTrace.NodeName)),
                new Col("端口", nameof(NodeTrace.Port), 70),
                new Col("ms", nameof(NodeTrace.DurationMs), 50),
                Col.Fill("信息", nameof(NodeTrace.Message)),
                Col.Fill("错误", nameof(NodeTrace.Error)))
            .RowColor(t => t.Error != null ? Ui.Danger : null);
        var rows = new System.Collections.ObjectModel.ObservableCollection<KeyValuePairRow>();
        var variables = new BoundGrid<KeyValuePairRow>(rows, false,
            Col.Fill("变量", nameof(KeyValuePairRow.Key)),
            Col.Fill("值", nameof(KeyValuePairRow.Value)));
        Bind.Observe(_vm.SimulationVariables, () =>
        {
            rows.Clear();
            foreach (var kv in _vm.SimulationVariables) rows.Add(new KeyValuePairRow(kv.Key, kv.Value));
        });
        var input = Ui.CodeBox(_vm, nameof(_vm.SimulationInput), 70);
        input.Dock = DockStyle.Fill;
        return Ui.Rows(
            ("auto", Ui.Lbl("输入变量 JSON（如 {\"CellCode\":\"A001\"}）。模拟运行使用模拟设备上下文，不写真实 PLC。")),
            ("80", input),
            ("auto", Ui.Bar(Ui.Btn("模拟运行", _vm.SimulateCommand, ButtonKind.Primary), Ui.Show(_vm, nameof(_vm.SimulationResult), bold: true))),
            ("*", Ui.Cols(("3*", trace), ("2*", variables))));
    }

    // ---------------- 节点属性面板 ----------------

    /// <summary>构建属性面板。</summary>
    private Control BuildPropertyPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4) };
        var bindings = new List<IDisposable>();
        var tip = new ToolTip();
        Bind.Observe(_vm, nameof(_vm.SelectedNode), () =>
        {
            bindings.ForEach(b => b.Dispose());
            bindings.Clear();
            host.SuspendLayout();
            foreach (Control c in host.Controls) c.Dispose();
            host.Controls.Clear();
            if (_vm.SelectedNode is not { } node)
            {
                host.Controls.Add(Ui.Lbl("在节点表或流程图中选择一个节点", color: Ui.Muted));
                host.ResumeLayout();
                return;
            }

            var stack = Ui.Stack(
                Ui.Title($"{node.TypeName}  ·  {node.Id}"),
                new Label { Text = node.Descriptor?.Description ?? "", AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Ui.Muted },
                Ui.Lbl("名称"));
            var name = new TextBox();
            bindings.Add(Bind.TextBox(name, node, nameof(FlowNodeViewModel.Name)));
            Ui.AddToStack(stack, name);
            Ui.AddToStack(stack, Ui.Lbl("节点超时 ms（0 不限，设置后出现 timeout 端口）"));
            var timeout = new TextBox();
            bindings.Add(Bind.TextBox(timeout, node, nameof(FlowNodeViewModel.TimeoutMs)));
            Ui.AddToStack(stack, timeout);

            foreach (var p in node.Properties)
            {
                var label = Ui.Lbl(p.DisplayName);
                tip.SetToolTip(label, p.Description);
                Ui.AddToStack(stack, label);
                Control editor;
                switch (p.Editor)
                {
                    case "Bool":
                        var check = new CheckBox { AutoSize = true, Text = p.Description };
                        bindings.Add(Bind.Checked(check, p, nameof(NodePropertyItem.Value)));
                        editor = check;
                        break;
                    case "Multi":
                        var code = Ui.Code();
                        code.Dock = DockStyle.None;
                        code.Height = 110;
                        bindings.Add(Bind.TextBox(code, p, nameof(NodePropertyItem.Value)));
                        editor = code;
                        break;
                    case "Combo":
                        var combo = new ComboBox();
                        bindings.Add(Bind.EditableCombo(combo, p.Suggestions, p, nameof(NodePropertyItem.Value)));
                        editor = combo;
                        break;
                    default:
                        var text = new TextBox();
                        bindings.Add(Bind.TextBox(text, p, nameof(NodePropertyItem.Value)));
                        editor = text;
                        break;
                }
                tip.SetToolTip(editor, p.Description);
                Ui.AddToStack(stack, editor);
            }

            Ui.AddToStack(stack, Ui.Lbl("输出端口", bold: true));
            var ports = new Label { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Ui.Primary };
            void UpdatePorts() => ports.Text = string.Join("   ", node.Ports);
            node.Ports.CollectionChanged += (_, _) => UpdatePorts();
            UpdatePorts();
            Ui.AddToStack(stack, ports);
            Ui.AddToStack(stack, Ui.Lbl("所有节点均可接 error 端口处理异常", color: Ui.Muted));
            host.Controls.Add(stack);
            host.ResumeLayout();
        });
        return host;
    }
}

/// <summary>键值一对行。</summary>
/// <param name="Key">键。</param>
/// <param name="Value">值。</param>
public sealed record KeyValuePairRow(string Key, string Value);
