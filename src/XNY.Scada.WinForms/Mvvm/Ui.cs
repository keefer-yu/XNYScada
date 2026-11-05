using System.Collections;
using System.ComponentModel;
using System.Windows.Input;

namespace XNY.Scada.WinForms.Mvvm;

/// <summary>键类别。</summary>
public enum ButtonKind
{
    /// <summary>默认。</summary>
    Default,
    /// <summary>主。</summary>
    Primary,
    /// <summary>危险。</summary>
    Danger
}

/// <summary>代码式界面构建工具：统一风格，页面代码只描述布局与绑定</summary>
public static class Ui
{
    /// <summary>主。</summary>
    public static readonly Color Primary = Color.FromArgb(0x32, 0x6C, 0xE5);
    /// <summary>危险。</summary>
    public static readonly Color Danger = Color.FromArgb(0xE5, 0x3E, 0x3E);
    /// <summary>良品。</summary>
    public static readonly Color Good = Color.FromArgb(0x2E, 0xB8, 0x5C);
    /// <summary>警告。</summary>
    public static readonly Color Warn = Color.FromArgb(0xF0, 0xA0, 0x20);
    /// <summary>弱化显示。</summary>
    public static readonly Color Muted = Color.FromArgb(0x71, 0x80, 0x96);
    /// <summary>标题返回。</summary>
    public static readonly Color HeaderBack = Color.FromArgb(0x1F, 0x2D, 0x3D);
    /// <summary>菜单返回。</summary>
    public static readonly Color MenuBack = Color.FromArgb(0x26, 0x34, 0x45);
    /// <summary>页面返回。</summary>
    public static readonly Color PageBack = Color.FromArgb(0xF0, 0xF2, 0xF5);
    /// <summary>代码字体。</summary>
    public static readonly Font CodeFont = new("Consolas", 10f);

    // ---------------- 基础控件 ----------------

    /// <summary>按钮。</summary>
    public static Button Btn(string text, ICommand command, ButtonKind kind = ButtonKind.Default, Func<object?>? parameter = null)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(72, 30),
            Padding = new Padding(6, 0, 6, 0),
            Margin = new Padding(0, 2, 6, 2),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        var back = kind switch { ButtonKind.Primary => Primary, ButtonKind.Danger => Danger, _ => Color.White };
        var fore = kind == ButtonKind.Default ? Color.Black : Color.White;
        var border = kind == ButtonKind.Default ? Color.FromArgb(0xC9, 0xD1, 0xDC) : back;
        void Style()
        {
            b.BackColor = b.Enabled ? back : Color.FromArgb(0xED, 0xF0, 0xF4);
            b.ForeColor = b.Enabled ? fore : Color.FromArgb(0xA0, 0xAE, 0xC0);
            b.FlatAppearance.BorderColor = b.Enabled ? border : Color.FromArgb(0xDD, 0xE2, 0xEA);
        }
        b.EnabledChanged += (_, _) => Style();
        Style();
        Bind.Command(b, command, parameter);
        return b;
    }

    /// <summary>标签。</summary>
    public static Label Lbl(string text, bool bold = false, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 8, 6, 0),
        ForeColor = color ?? Color.FromArgb(0x2D, 0x37, 0x48),
        Font = bold ? new Font(Control.DefaultFont, FontStyle.Bold) : Control.DefaultFont
    };

    /// <summary>标题。</summary>
    public static Label Title(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 4, 6, 6),
        Font = new Font(Control.DefaultFont.FontFamily, 10.5f, FontStyle.Bold)
    };

    /// <summary>只读文本，随属性刷新</summary>
    public static Label Show(INotifyPropertyChanged source, string property, Func<object?, string>? format = null, bool bold = false, Color? color = null)
    {
        var l = Lbl("", bold, color);
        Bind.Text(l, source, property, format);
        return l;
    }

    /// <summary>文本。</summary>
    public static TextBox Text(INotifyPropertyChanged source, string property, int width = 140)
    {
        var t = new TextBox { Width = width, Margin = new Padding(0, 4, 8, 2) };
        Bind.TextBox(t, source, property);
        return t;
    }

    /// <summary>多行代码/JSON 编辑框（Dock 填充）</summary>
    public static TextBox Code(INotifyPropertyChanged? source = null, string? property = null, bool readOnly = false)
    {
        var t = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            AcceptsReturn = true,
            AcceptsTab = true,
            Font = CodeFont,
            Dock = DockStyle.Fill,
            ReadOnly = readOnly,
            BackColor = readOnly ? Color.FromArgb(0xF7, 0xF9, 0xFC) : Color.White
        };
        if (source != null && property != null)
        {
            if (readOnly) Bind.Text(t, source, property, v => (v?.ToString() ?? "").ReplaceLineEndings("\r\n"));
            else Bind.TextBox(t, source, property);
        }
        return t;
    }

    /// <summary>检查。</summary>
    public static CheckBox Check(string text, INotifyPropertyChanged source, string property)
    {
        var c = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 6, 12, 2) };
        Bind.Checked(c, source, property);
        return c;
    }

    /// <summary>下拉框。</summary>
    public static ComboBox Combo(IEnumerable items, INotifyPropertyChanged source, string property, int width = 140,
        Func<object, string>? display = null, Func<object, object?>? valueOf = null)
    {
        var c = new ComboBox { Width = width, Margin = new Padding(0, 4, 8, 2) };
        Bind.Combo(c, items, source, property, display, valueOf);
        return c;
    }

    /// <summary>日期。</summary>
    public static DateTimePicker Date(INotifyPropertyChanged source, string property)
    {
        var d = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Margin = new Padding(0, 4, 8, 2) };
        Bind.Date(d, source, property);
        return d;
    }

    // ---------------- 布局 ----------------

    /// <summary>工具栏：自动换行的横向排列</summary>
    public static FlowLayoutPanel Bar(params Control[] controls)
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 2, 4, 2),
            Margin = new Padding(0)
        };
        p.Controls.AddRange(controls);
        return p;
    }

    /// <summary>带标题的卡片区块</summary>
    public static GroupBox Card(string title, Control content)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6), Margin = new Padding(4), BackColor = Color.White };
        content.Dock = DockStyle.Fill;
        g.Controls.Add(content);
        return g;
    }

    /// <summary>添加样式。</summary>
    private static void AddStyle(TableLayoutStyleCollection styles, string size, bool rows)
    {
        TableLayoutStyle style = size switch
        {
            "auto" => rows ? new RowStyle(SizeType.AutoSize) : new ColumnStyle(SizeType.AutoSize),
            _ when size.EndsWith('*') => rows
                ? new RowStyle(SizeType.Percent, size.Length == 1 ? 100 : float.Parse(size[..^1]) * 100)
                : new ColumnStyle(SizeType.Percent, size.Length == 1 ? 100 : float.Parse(size[..^1]) * 100),
            _ => rows ? new RowStyle(SizeType.Absolute, float.Parse(size)) : new ColumnStyle(SizeType.Absolute, float.Parse(size))
        };
        styles.Add(style);
    }

    /// <summary>纵向分区：尺寸 "auto" / "120"（像素）/ "*" / "2*"（比例）</summary>
    public static TableLayoutPanel Rows(params (string Size, Control Control)[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows.Length; i++)
        {
            AddStyle(t.RowStyles, rows[i].Size, true);
            rows[i].Control.Dock = DockStyle.Fill;
            if (rows[i].Size == "auto" && rows[i].Control is GroupBox box)
            {
                box.AutoSize = true;
                box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            }
            t.Controls.Add(rows[i].Control, 0, i);
        }
        return t;
    }

    /// <summary>横向分区</summary>
    public static TableLayoutPanel Cols(params (string Size, Control Control)[] cols)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = cols.Length, Margin = new Padding(0) };
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < cols.Length; i++)
        {
            AddStyle(t.ColumnStyles, cols[i].Size, false);
            cols[i].Control.Dock = DockStyle.Fill;
            t.Controls.Add(cols[i].Control, i, 0);
        }
        return t;
    }

    /// <summary>表单：左标签右编辑器</summary>
    public static TableLayoutPanel Form(int labelWidth, params (string Label, Control Editor)[] fields)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = fields.Length, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < fields.Length; i++)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = Lbl(fields[i].Label);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            t.Controls.Add(label, 0, i);
            var editor = fields[i].Editor;
            if (editor is TextBox or ComboBox) editor.Dock = DockStyle.Fill;
            t.Controls.Add(editor, 1, i);
        }
        return t;
    }

    /// <summary>纵向堆叠（宽度撑满），可放入滚动容器</summary>
    public static TableLayoutPanel Stack(params Control[] controls)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var c in controls) AddToStack(t, c);
        return t;
    }

    /// <summary>添加到栈。</summary>
    public static void AddToStack(TableLayoutPanel stack, Control c)
    {
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (c is not CheckBox and not Button and not Label) c.Dock = DockStyle.Fill;
        stack.Controls.Add(c, 0, stack.RowCount++);
    }

    /// <summary>滚动。</summary>
    public static Panel Scroll(Control content)
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        content.Dock = DockStyle.Top;
        p.Controls.Add(content);
        return p;
    }

    /// <summary>标签页。</summary>
    public static TabControl Tabs(params (string Title, Control Content)[] pages)
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
        foreach (var (title, content) in pages)
        {
            var page = new TabPage(title) { Padding = new Padding(4), BackColor = Color.White };
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
        }
        return tabs;
    }

    /// <summary>固定高度的代码框（用于表单内）</summary>
    public static TextBox CodeBox(INotifyPropertyChanged source, string property, int height)
    {
        var t = Code(source, property);
        t.Dock = DockStyle.None;
        t.Height = height;
        return t;
    }
}
