using System.Collections.Specialized;
using System.ComponentModel;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

/// <summary>看板页面。</summary>
public sealed class DashboardPage : UserControl
{
    /// <summary>视图模型。</summary>
    private readonly DashboardViewModel _vm;

    /// <summary>创建看板页面。</summary>
    public DashboardPage(DashboardViewModel vm)
    {
        _vm = vm;

        var dot = new Label { Text = "●", AutoSize = true, Font = new Font(Font.FontFamily, 14f), Margin = new Padding(0, 2, 4, 0) };
        Bind.Observe(vm, nameof(vm.EngineRunning), () => dot.ForeColor = vm.EngineRunning ? Ui.Good : Color.Gray);
        var manual = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(24, 0, 0, 0) };
        Bind.Observe(vm.ManualFlows, () =>
        {
            manual.Controls.Clear();
            foreach (var code in vm.ManualFlows) manual.Controls.Add(Ui.Btn(code, vm.TriggerFlowCommand, parameter: () => code));
        });
        var toolbar = Ui.Bar(
            dot,
            Ui.Show(vm, nameof(vm.MachineTypeName), bold: true),
            Ui.Btn("启动生产", vm.StartProductionCommand, ButtonKind.Primary),
            Ui.Btn("停止生产", vm.StopProductionCommand, ButtonKind.Danger),
            Ui.Btn("计数清零", vm.ResetCountersCommand),
            Ui.Btn("确认全部报警", vm.AcknowledgeAllCommand),
            manual);

        var kpis = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Height = 92 };
        for (var i = 0; i < 6; i++) kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        kpis.Controls.Add(Kpi("产量", nameof(vm.Total), null), 0, 0);
        kpis.Controls.Add(Kpi("OK", nameof(vm.Ok), Ui.Good), 1, 0);
        kpis.Controls.Add(Kpi("NG", nameof(vm.Ng), Ui.Danger), 2, 0);
        kpis.Controls.Add(Kpi("良率 %", nameof(vm.Yield), null), 3, 0);
        kpis.Controls.Add(Kpi("UPH", nameof(vm.Uph), null), 4, 0);
        var oee = Kpi("OEE %", nameof(vm.Oee), null);
        var apq = Ui.Lbl("", color: Ui.Muted);
        void UpdateApq() => apq.Text = $"A {vm.Availability}  P {vm.Performance}  Q {vm.Quality}";
        Bind.Observe(vm, nameof(vm.Quality), UpdateApq);
        Bind.Observe(vm, nameof(vm.Availability), UpdateApq);
        apq.Dock = DockStyle.Bottom;
        oee.Controls[0].Controls.Add(apq);
        kpis.Controls.Add(oee, 5, 0);

        var devices = new BoundGrid<DeviceStatusItem>(vm.Devices, false,
                new Col("状态", nameof(DeviceStatusItem.StatusText), 50),
                Col.Fill("设备", nameof(DeviceStatusItem.Name)),
                new Col("驱动", nameof(DeviceStatusItem.DriverKey), 90),
                new Col("ms", nameof(DeviceStatusItem.LastScanMs), 45))
            .RowColor(d => d.Connected ? null : Ui.Danger);
        var keyTags = new BoundGrid<TagValueItem>(vm.KeyTags, false,
            Col.Fill("关键点位", nameof(TagValueItem.FullName)),
            new Col("值", nameof(TagValueItem.Value), 80));
        var left = Ui.Rows(("*", Ui.Card("设备状态", devices)), ("*", Ui.Card("关键点位", keyTags)));

        var trayTitle = new Label { Dock = DockStyle.Top, Height = 22 };
        void UpdateTrayTitle() => trayTitle.Text = $"当前托盘 {vm.CurrentTrayCode}    已装 {vm.TrayCellCount}";
        Bind.Observe(vm, nameof(vm.CurrentTrayCode), UpdateTrayTitle);
        Bind.Observe(vm, nameof(vm.TrayCellCount), UpdateTrayTitle);
        var trayPanel = new Panel { Dock = DockStyle.Fill };
        trayPanel.Controls.Add(new TrayMatrix(vm) { Dock = DockStyle.Fill });
        trayPanel.Controls.Add(trayTitle);
        var trayCard = Ui.Card("托盘矩阵", trayPanel);

        var bins = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var binBindings = new List<IDisposable>();
        Bind.Observe(vm.Bins, () =>
        {
            binBindings.ForEach(b => b.Dispose());
            binBindings.Clear();
            foreach (Control c in bins.Controls) c.Dispose();
            bins.Controls.Clear();
            foreach (var bin in vm.Bins) bins.Controls.Add(BinCard(bin, binBindings));
        });
        var binCard = Ui.Card("档位料箱", bins);

        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(binCard);
        middle.Controls.Add(trayCard);
        void Arrange()
        {
            trayCard.Visible = vm.ShowTray;
            binCard.Visible = vm.ShowBins;
            trayCard.Dock = vm.ShowBins ? DockStyle.Top : DockStyle.Fill;
            trayCard.Height = middle.Height / 2;
            binCard.Dock = DockStyle.Fill;
        }
        Bind.Observe(vm, nameof(vm.ShowTray), Arrange);
        Bind.Observe(vm, nameof(vm.ShowBins), Arrange);
        middle.Resize += (_, _) => Arrange();

        var ng = new BoundGrid<NgCountItem>(vm.NgCounts, false,
            Col.Fill("NG 代码", nameof(NgCountItem.Code)),
            new Col("数量", nameof(NgCountItem.Count), 60));

        var records = new BoundGrid<CellRecordEntity>(vm.RecentRecords, false,
                new Col("时间", nameof(CellRecordEntity.CreateTime), 70, "HH:mm:ss"),
                Col.Fill("电芯码", nameof(CellRecordEntity.CellCode)),
                new Col("托盘", nameof(CellRecordEntity.TrayCode), 110),
                new Col("电压", nameof(CellRecordEntity.Voltage), 65),
                new Col("内阻", nameof(CellRecordEntity.InternalResistance), 65),
                new Col("档位", nameof(CellRecordEntity.BinName), 80),
                new Col("OK", nameof(CellRecordEntity.IsOk), 40),
                new Col("NG", nameof(CellRecordEntity.NgCode), 80))
            .RowColor(r => r.IsOk ? null : Ui.Danger);
        var alarms = new BoundGrid<AlarmRecordEntity>(vm.ActiveAlarms, false,
                new Col("时间", nameof(AlarmRecordEntity.RaisedAt), 70, "HH:mm:ss"),
                new Col("级别", nameof(AlarmRecordEntity.Level), 60),
                Col.Fill("内容", nameof(AlarmRecordEntity.Message)))
            .RowColor(a => Colors.Level(a.Level));

        Controls.Add(Ui.Rows(
            ("auto", Ui.Card("", toolbar)),
            ("100", kpis),
            ("*", Ui.Cols(("300", left), ("*", middle), ("240", Ui.Card("NG 统计", ng)))),
            ("240", Ui.Cols(("2*", Ui.Card("实时生产记录", records)), ("*", Ui.Card("当前报警", alarms))))));
    }

    /// <summary>指标。</summary>
    private GroupBox Kpi(string caption, string property, Color? color)
    {
        var value = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 22f, FontStyle.Bold), ForeColor = color ?? Color.FromArgb(0x1A, 0x20, 0x2C)
        };
        Bind.Text(value, _vm, property);
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(value);
        return Ui.Card(caption, panel);
    }

    /// <summary>档位卡。</summary>
    private Control BinCard(BinItem bin, List<IDisposable> bindings)
    {
        var card = new Panel { Width = 160, Height = 112, Margin = new Padding(4), Padding = new Padding(8), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var name = new Label { Dock = DockStyle.Top, Height = 20, Font = new Font(Font, FontStyle.Bold) };
        var count = new Label { Dock = DockStyle.Top, Height = 34, Font = new Font(Font.FontFamily, 16f, FontStyle.Bold) };
        var bar = new ProgressBar { Dock = DockStyle.Top, Height = 8, Maximum = 100 };
        var reset = new Button { Text = "换箱清零", Dock = DockStyle.Bottom, Height = 26, FlatStyle = FlatStyle.Flat };
        bindings.Add(Bind.Command(reset, _vm.ResetBinCommand, () => bin));
        bindings.Add(Bind.Text(name, bin, nameof(BinItem.Name)));
        void Update()
        {
            count.Text = $"{bin.Count} / {bin.Capacity}";
            bar.Value = (int)Math.Clamp(bin.Percent, 0, 100);
            card.BackColor = bin.IsFull ? Color.FromArgb(0xFF, 0xE5, 0xE5) : Color.White;
        }
        bindings.Add(Bind.Observe(bin, nameof(BinItem.Count), Update));
        bindings.Add(Bind.Observe(bin, nameof(BinItem.Capacity), Update));
        bindings.Add(Bind.Observe(bin, nameof(BinItem.IsFull), Update));
        card.Controls.Add(reset);
        card.Controls.Add(bar);
        card.Controls.Add(count);
        card.Controls.Add(name);
        return card;
    }

    /// <summary>托盘矩阵：按行列绘制穴位，有电芯为绿色</summary>
    private sealed class TrayMatrix : Panel
    {
        /// <summary>视图模型。</summary>
        private readonly DashboardViewModel _vm;
        /// <summary>提示。</summary>
        private readonly ToolTip _tip = new();

        /// <summary>创建托盘矩阵。</summary>
        public TrayMatrix(DashboardViewModel vm)
        {
            _vm = vm;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            PropertyChangedEventHandler cellChanged = (_, _) => Invalidate();
            vm.TrayCells.CollectionChanged += (_, e) =>
            {
                foreach (var i in e.NewItems?.OfType<TrayCellItem>() ?? []) i.PropertyChanged += cellChanged;
                foreach (var i in e.OldItems?.OfType<TrayCellItem>() ?? []) i.PropertyChanged -= cellChanged;
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    foreach (var i in vm.TrayCells) i.PropertyChanged += cellChanged;
                }
                Invalidate();
            };
            foreach (var i in vm.TrayCells) i.PropertyChanged += cellChanged;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(vm.TrayCols) or nameof(vm.TrayRows)) Invalidate();
            };
            MouseMove += (_, e) =>
            {
                var cell = HitTest(e.Location);
                var text = cell?.CellCode ?? "";
                if (_tip.GetToolTip(this) != text) _tip.SetToolTip(this, text);
            };
        }

        /// <summary>单元格矩形。</summary>
        private RectangleF CellRect(int index)
        {
            var cols = Math.Max(1, _vm.TrayCols);
            var rows = Math.Max(1, _vm.TrayRows);
            var w = (Width - 4f) / cols;
            var h = (Height - 4f) / rows;
            return new RectangleF(2 + index % cols * w, 2 + index / cols * h, w, h);
        }

        /// <summary>命中测试测试。</summary>
        private TrayCellItem? HitTest(Point p)
        {
            for (var i = 0; i < _vm.TrayCells.Count; i++)
            {
                if (CellRect(i).Contains(p)) return _vm.TrayCells[i];
            }
            return null;
        }

        /// <summary>处理绘制。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var full = new SolidBrush(Color.FromArgb(0x9A, 0xE6, 0xB4));
            using var empty = new SolidBrush(Color.FromArgb(0xF7, 0xF9, 0xFC));
            using var border = new Pen(Color.FromArgb(0xC9, 0xD1, 0xDC));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (var i = 0; i < _vm.TrayCells.Count; i++)
            {
                var r = CellRect(i);
                r.Inflate(-2, -2);
                if (r.Width <= 0 || r.Height <= 0) continue;
                g.FillRectangle(_vm.TrayCells[i].HasCell ? full : empty, r);
                g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
                g.DrawString(_vm.TrayCells[i].Position.ToString(), Font, Brushes.DimGray, r, format);
            }
        }
    }
}
