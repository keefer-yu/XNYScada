using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using XNY.Scada.Flow.Nodes;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Presentation.ViewModels;

namespace XNY.Scada.Wpf.Views;

/// <summary>
/// 图形化流程编辑：拖入节点、拖动节点、从输出端口拖到目标节点连线、点击连线选中、Del 删除、Ctrl+Z 撤销、Ctrl+滚轮缩放。
/// 所有编辑都委托给 <see cref="FlowDesignerViewModel"/>，视图只负责鼠标交互与坐标换算。
/// </summary>
public partial class FlowDesignerView
{
    /// <summary>拖动格式。</summary>
    private const string DragFormat = "xny-flow-node";
    /// <summary>工具面板下。</summary>
    private Point _paletteDown;
    /// <summary>拖动节点。</summary>
    private FlowNodeViewModel? _dragNode;
    /// <summary>拖动偏移。</summary>
    private Vector _dragOffset;
    /// <summary>已移动。</summary>
    private bool _moved;
    /// <summary>连接从。</summary>
    private FlowNodeViewModel? _connectFrom;
    /// <summary>连接端口号。</summary>
    private string? _connectPort;

    /// <summary>创建流程设计器视图。</summary>
    public FlowDesignerView()
    {
        InitializeComponent();
    }

    /// <summary>视图模型。</summary>
    private FlowDesignerViewModel? Vm => DataContext as FlowDesignerViewModel;

    /// <summary>查找数据。</summary>
    private static T? FindData<T>(DependencyObject? d) where T : class
    {
        while (d != null)
        {
            if (d is FrameworkElement { DataContext: T t }) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------- 节点面板 ----------

    /// <summary>处理工具面板鼠标下。</summary>
    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e)
    {
        _paletteDown = e.GetPosition(this);
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: NodeDescriptor d } && Vm != null)
        {
            var zoom = ZoomSlider.Value;
            Vm.AddNode(d.Type,
                (CanvasScroll.HorizontalOffset + CanvasScroll.ViewportWidth / 2) / zoom - FlowNodeViewModel.Width / 2,
                (CanvasScroll.VerticalOffset + CanvasScroll.ViewportHeight / 2) / zoom - FlowNodeViewModel.Height / 2);
        }
    }

    /// <summary>处理工具面板鼠标移动。</summary>
    private void OnPaletteMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: NodeDescriptor d }) return;
        var delta = e.GetPosition(this) - _paletteDown;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, d.Type), DragDropEffects.Copy);
    }

    /// <summary>处理画布投影。</summary>
    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (Vm == null || e.Data.GetData(DragFormat) is not string type) return;
        var pos = e.GetPosition(CanvasRoot);
        Vm.AddNode(type, pos.X - FlowNodeViewModel.Width / 2, pos.Y - 20);
        Focus();
    }

    // ---------- 节点拖动 ----------

    /// <summary>处理节点鼠标下。</summary>
    private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null || sender is not FrameworkElement { DataContext: FlowNodeViewModel node } el) return;
        Vm.SelectedNode = node;
        Focus();
        if (!Vm.CanEdit) return;
        _dragNode = node;
        _dragOffset = e.GetPosition(CanvasRoot) - new Point(node.X, node.Y);
        _moved = false;
        el.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>处理节点鼠标移动。</summary>
    private void OnNodeMouseMove(object sender, MouseEventArgs e)
    {
        if (Vm == null || _dragNode == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(CanvasRoot) - _dragOffset;
        if (!_moved)
        {
            if (Math.Abs(pos.X - _dragNode.X) < 3 && Math.Abs(pos.Y - _dragNode.Y) < 3) return;
            Vm.BeginMove();
            _moved = true;
        }
        Vm.MoveNode(_dragNode, pos.X, pos.Y);
    }

    /// <summary>处理节点鼠标上。</summary>
    private void OnNodeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragNode == null) return;
        ((UIElement)sender).ReleaseMouseCapture();
        if (_moved) Vm?.EndMove();
        _dragNode = null;
        e.Handled = true;
    }

    // ---------- 端口连线 ----------

    /// <summary>处理端口号鼠标下。</summary>
    private void OnPortMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { CanEdit: true } || sender is not FrameworkElement { DataContext: PortInfo port } el) return;
        var node = FindData<FlowNodeViewModel>(VisualTreeHelper.GetParent(el));
        if (node == null) return;
        _connectFrom = node;
        _connectPort = port.Name;
        var start = new Point(node.X + port.Left + 6, node.Y + FlowNodeViewModel.Height);
        DragLine.X1 = DragLine.X2 = start.X;
        DragLine.Y1 = DragLine.Y2 = start.Y;
        DragLine.Visibility = Visibility.Visible;
        CanvasRoot.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>处理画布鼠标移动。</summary>
    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_connectFrom == null) return;
        var pos = e.GetPosition(CanvasRoot);
        DragLine.X2 = pos.X;
        DragLine.Y2 = pos.Y;
    }

    /// <summary>处理画布鼠标上。</summary>
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_connectFrom == null || _connectPort == null) return;
        CanvasRoot.ReleaseMouseCapture();
        DragLine.Visibility = Visibility.Collapsed;
        var pos = e.GetPosition(CanvasRoot);
        FlowNodeViewModel? target = null;
        VisualTreeHelper.HitTest(CanvasRoot, null, r =>
        {
            target = FindData<FlowNodeViewModel>(r.VisualHit);
            return target == null ? HitTestResultBehavior.Continue : HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(pos));
        if (target != null) Vm?.Connect(_connectFrom, _connectPort, target);
        _connectFrom = null;
        _connectPort = null;
    }

    /// <summary>处理画布鼠标下。</summary>
    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        Vm.SelectedNode = null;
        Vm.SelectedEdge = null;
        Focus();
    }

    /// <summary>处理边鼠标下。</summary>
    private void OnEdgeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null || sender is not FrameworkElement { DataContext: FlowEdgeViewModel edge }) return;
        Vm.SelectedEdge = edge;
        Focus();
        e.Handled = true;
    }

    // ---------- 键盘 / 缩放 / 校验定位 ----------

    /// <summary>处理键下。</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null || e.OriginalSource is TextBoxBase or ComboBox or PasswordBox) return;
        if (e.Key == Key.Delete)
        {
            Vm.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Vm.UndoCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Vm.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>处理画布滚轮。</summary>
    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (e.Delta > 0 ? 0.1 : -0.1), ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    /// <summary>处理问题双击。</summary>
    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null || sender is not ListBox { SelectedItem: ValidationIssue { NodeId: { } id } }) return;
        var node = Vm.Nodes.FirstOrDefault(n => n.Id == id);
        if (node == null) return;
        Vm.SelectedNode = node;
        CanvasScroll.ScrollToHorizontalOffset(Math.Max(0, node.X * ZoomSlider.Value - 100));
        CanvasScroll.ScrollToVerticalOffset(Math.Max(0, node.Y * ZoomSlider.Value - 100));
    }
}
