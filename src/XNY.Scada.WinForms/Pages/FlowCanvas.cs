using System.ComponentModel;
using System.Drawing.Drawing2D;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

/// <summary>GDI+ 流程图：与 WPF 画布共享同一套节点/连线坐标模型</summary>
internal sealed class FlowCanvas : Panel
{
    /// <summary>节点W。</summary>
    private const float NodeW = (float)FlowNodeViewModel.Width;
    /// <summary>节点H。</summary>
    private const float NodeH = (float)FlowNodeViewModel.Height;
    /// <summary>端口号半径。</summary>
    private const float PortRadius = 6;

    /// <summary>视图模型。</summary>
    private readonly FlowDesignerViewModel _vm;
    /// <summary>已订阅。</summary>
    private readonly HashSet<INotifyPropertyChanged> _subscribed = [];
    /// <summary>缩放。</summary>
    private float _zoom = 1f;
    /// <summary>拖动节点。</summary>
    private FlowNodeViewModel? _dragNode;
    /// <summary>拖动偏移。</summary>
    private PointF _dragOffset;
    /// <summary>连接从。</summary>
    private FlowNodeViewModel? _connectFrom;
    /// <summary>连接端口号。</summary>
    private string? _connectPort;
    /// <summary>连接点。</summary>
    private PointF _connectPoint;

    /// <summary>创建流画布。</summary>
    public FlowCanvas(FlowDesignerViewModel vm)
    {
        _vm = vm;
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(0xFA, 0xFB, 0xFD);
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        vm.Nodes.CollectionChanged += (_, _) => Resubscribe();
        vm.Edges.CollectionChanged += (_, _) => Resubscribe();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.CanvasWidth) or nameof(vm.CanvasHeight) or nameof(vm.IsLoaded)) UpdateSize();
        };
        Resubscribe();
        UpdateSize();
    }

    /// <summary>缩放。</summary>
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.3f, 2f);
            UpdateSize();
            Invalidate();
        }
    }

    /// <summary>视图中。</summary>
    public PointF ViewCenter()
    {
        var p = ToModel(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        return new PointF(Math.Max(0, p.X - NodeW / 2), Math.Max(0, p.Y - NodeH / 2));
    }

    /// <summary>更新大小。</summary>
    private void UpdateSize() => AutoScrollMinSize = new Size((int)(_vm.CanvasWidth * _zoom), (int)(_vm.CanvasHeight * _zoom));

    /// <summary>重新订阅。</summary>
    private void Resubscribe()
    {
        var current = _vm.Nodes.Cast<INotifyPropertyChanged>().Concat(_vm.Edges).ToHashSet();
        foreach (var gone in _subscribed.Except(current).ToList())
        {
            gone.PropertyChanged -= OnItemChanged;
            _subscribed.Remove(gone);
        }
        foreach (var added in current.Except(_subscribed).ToList())
        {
            added.PropertyChanged += OnItemChanged;
            _subscribed.Add(added);
        }
        Invalidate();
    }

    /// <summary>当项变化时更新关联状态。</summary>
    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();

    /// <summary>转换为型号。</summary>
    private PointF ToModel(Point p) => new((p.X - AutoScrollPosition.X) / _zoom, (p.Y - AutoScrollPosition.Y) / _zoom);

    /// <summary>端口号中。</summary>
    private static PointF PortCenter(FlowNodeViewModel n, string port) => new((float)(n.X + n.PortOffset(port)), (float)n.Y + NodeH);

    /// <summary>节点矩形。</summary>
    private static RectangleF NodeRect(FlowNodeViewModel n) => new((float)n.X, (float)n.Y, NodeW, NodeH);

    /// <summary>贝塞尔。</summary>
    private static PointF[] Bezier(FlowEdgeViewModel e)
    {
        var dy = (float)Math.Max(40, Math.Abs(e.Y2 - e.Y1) / 2);
        return
        [
            new((float)e.X1, (float)e.Y1), new((float)e.X1, (float)e.Y1 + dy),
            new((float)e.X2, (float)e.Y2 - dy), new((float)e.X2, (float)e.Y2)
        ];
    }

    /// <summary>分类颜色。</summary>
    private static Color CategoryColor(string category) => category switch
    {
        "逻辑" => Color.FromArgb(0x5A, 0x67, 0xD8),
        "通讯" => Color.FromArgb(0x31, 0x97, 0x95),
        "业务" => Color.FromArgb(0xDD, 0x6B, 0x20),
        "高级" => Color.FromArgb(0x80, 0x5A, 0xD5),
        _ => Color.FromArgb(0x71, 0x80, 0x96)
    };

    // ---------------- 绘制 ----------------

    /// <summary>处理绘制。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        g.ScaleTransform(_zoom, _zoom);

        if (!_vm.IsLoaded)
        {
            g.ResetTransform();
            TextRenderer.DrawText(g, "请先打开或新建流程", new Font(Font.FontFamily, 14f), ClientRectangle, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        using (var gridPen = new Pen(Color.FromArgb(0xED, 0xF0, 0xF5)))
        {
            for (var x = 0; x < _vm.CanvasWidth; x += 20) g.DrawLine(gridPen, x, 0, x, (float)_vm.CanvasHeight);
            for (var y = 0; y < _vm.CanvasHeight; y += 20) g.DrawLine(gridPen, 0, y, (float)_vm.CanvasWidth, y);
        }

        using var small = new Font(Font.FontFamily, 7.5f);
        foreach (var edge in _vm.Edges) DrawEdge(g, edge, small);
        foreach (var node in _vm.Nodes) DrawNode(g, node, small);

        if (_connectFrom != null && _connectPort != null)
        {
            using var pen = new Pen(Ui.Primary, 2) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, PortCenter(_connectFrom, _connectPort), _connectPoint);
        }
    }

    /// <summary>绘制边。</summary>
    private static void DrawEdge(Graphics g, FlowEdgeViewModel edge, Font font)
    {
        var pts = Bezier(edge);
        using var pen = new Pen(edge.IsSelected ? Ui.Primary : Color.FromArgb(0x7A, 0x86, 0x99), edge.IsSelected ? 3 : 2);
        g.DrawBezier(pen, pts[0], pts[1], pts[2], pts[3]);
        var end = pts[3];
        g.DrawLines(pen, [new PointF(end.X - 5, end.Y - 9), end, new PointF(end.X + 5, end.Y - 9)]);
        var label = edge.Port;
        var size = g.MeasureString(label, font);
        var mid = new PointF((float)edge.LabelX - size.Width / 2, (float)edge.LabelY - size.Height / 2);
        g.FillRectangle(Brushes.White, mid.X, mid.Y, size.Width, size.Height);
        g.DrawString(label, font, Brushes.DimGray, mid);
    }

    /// <summary>绘制节点。</summary>
    private void DrawNode(Graphics g, FlowNodeViewModel node, Font small)
    {
        var r = NodeRect(node);
        var borderColor = node.IsSelected ? Ui.Primary
            : node.HasError ? Ui.Danger
            : node.ExecState switch
            {
                NodeExecState.Running => Ui.Primary,
                NodeExecState.Done => Ui.Good,
                NodeExecState.Failed => Ui.Danger,
                _ => Color.FromArgb(0xC9, 0xD1, 0xDC)
            };
        using (var path = RoundRect(r, 5))
        {
            g.FillPath(Brushes.White, path);
            using var header = new SolidBrush(CategoryColor(node.Category));
            g.SetClip(new RectangleF(r.X, r.Y, r.Width, 22));
            g.FillPath(header, path);
            g.ResetClip();
            using var border = new Pen(borderColor, node.IsSelected || node.ExecState != NodeExecState.None || node.HasError ? 2.5f : 1.5f);
            g.DrawPath(border, path);
        }

        var title = (node.IsStart ? "▶ " : "") + node.Title;
        using var titleFont = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(title, titleFont, Brushes.White, new RectangleF(r.X + 6, r.Y + 3, r.Width - 22, 18), fmt);
        if (node.HasBreakpoint)
        {
            using var bp = new SolidBrush(Color.FromArgb(0xFF, 0x6B, 0x6B));
            g.FillEllipse(bp, r.Right - 15, r.Y + 6, 10, 10);
        }
        g.DrawString($"{node.Id} · {node.TypeName}", small, Brushes.Gray, new RectangleF(r.X + 6, r.Y + 24, r.Width - 10, 14), fmt);
        using var summaryBrush = new SolidBrush(Color.FromArgb(0x4A, 0x55, 0x68));
        g.DrawString(node.Summary, Font, summaryBrush, new RectangleF(r.X + 6, r.Y + 38, r.Width - 10, 16), fmt);

        using var inPen = new Pen(Color.FromArgb(0x7A, 0x86, 0x99), 1.5f);
        g.FillEllipse(Brushes.White, r.X + r.Width / 2 - 5, r.Y - 5, 10, 10);
        g.DrawEllipse(inPen, r.X + r.Width / 2 - 5, r.Y - 5, 10, 10);

        using var portBrush = new SolidBrush(Ui.Primary);
        using var errorBrush = new SolidBrush(Ui.Danger);
        foreach (var port in node.Ports)
        {
            var c = PortCenter(node, port);
            g.FillEllipse(port == "error" ? errorBrush : portBrush, c.X - PortRadius, c.Y - PortRadius, PortRadius * 2, PortRadius * 2);
            g.DrawEllipse(Pens.White, c.X - PortRadius, c.Y - PortRadius, PortRadius * 2, PortRadius * 2);
        }
    }

    /// <summary>四舍五入矩形。</summary>
    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---------------- 交互 ----------------

    /// <summary>命中测试端口号。</summary>
    private (FlowNodeViewModel Node, string Port)? HitPort(PointF p)
    {
        foreach (var n in _vm.Nodes.Reverse())
        {
            foreach (var port in n.Ports)
            {
                var c = PortCenter(n, port);
                if (Math.Abs(c.X - p.X) <= PortRadius + 2 && Math.Abs(c.Y - p.Y) <= PortRadius + 2) return (n, port);
            }
        }
        return null;
    }

    /// <summary>命中测试节点。</summary>
    private FlowNodeViewModel? HitNode(PointF p) => _vm.Nodes.Reverse().FirstOrDefault(n => NodeRect(n).Contains(p));

    /// <summary>命中测试边。</summary>
    private FlowEdgeViewModel? HitEdge(PointF p)
    {
        foreach (var e in _vm.Edges)
        {
            var b = Bezier(e);
            for (var i = 0; i <= 24; i++)
            {
                var t = i / 24f;
                var u = 1 - t;
                var x = u * u * u * b[0].X + 3 * u * u * t * b[1].X + 3 * u * t * t * b[2].X + t * t * t * b[3].X;
                var y = u * u * u * b[0].Y + 3 * u * u * t * b[1].Y + 3 * u * t * t * b[2].Y + t * t * t * b[3].Y;
                if (Math.Abs(x - p.X) < 6 && Math.Abs(y - p.Y) < 6) return e;
            }
        }
        return null;
    }

    /// <summary>处理鼠标下。</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || !_vm.IsLoaded) return;
        var p = ToModel(e.Location);
        if (_vm.CanEdit && HitPort(p) is { } hit)
        {
            _connectFrom = hit.Node;
            _connectPort = hit.Port;
            _connectPoint = p;
            Capture = true;
            return;
        }
        if (HitNode(p) is { } node)
        {
            _vm.SelectedNode = node;
            if (_vm.CanEdit)
            {
                _dragNode = node;
                _dragOffset = new PointF(p.X - (float)node.X, p.Y - (float)node.Y);
                _vm.BeginMove();
                Capture = true;
            }
            return;
        }
        if (HitEdge(p) is { } edge)
        {
            _vm.SelectedEdge = edge;
            return;
        }
        _vm.SelectedNode = null;
        _vm.SelectedEdge = null;
    }

    /// <summary>处理鼠标移动。</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToModel(e.Location);
        if (_dragNode != null)
        {
            _vm.MoveNode(_dragNode, p.X - _dragOffset.X, p.Y - _dragOffset.Y);
        }
        else if (_connectFrom != null)
        {
            _connectPoint = p;
            Invalidate();
        }
        else
        {
            Cursor = HitPort(p) != null ? Cursors.Cross : HitNode(p) != null ? Cursors.SizeAll : Cursors.Default;
        }
    }

    /// <summary>处理鼠标上。</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        if (_dragNode != null)
        {
            _dragNode = null;
            _vm.EndMove();
        }
        if (_connectFrom != null && _connectPort != null)
        {
            if (HitNode(ToModel(e.Location)) is { } target) _vm.Connect(_connectFrom, _connectPort, target);
            _connectFrom = null;
            _connectPort = null;
            Invalidate();
        }
    }

    /// <summary>处理鼠标滚轮。</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            Zoom += e.Delta > 0 ? 0.1f : -0.1f;
            return;
        }
        base.OnMouseWheel(e);
    }

    /// <summary>处理键下。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete) Bind.Execute(_vm.DeleteSelectedCommand);
    }

    /// <summary>处理滚动。</summary>
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }
}
