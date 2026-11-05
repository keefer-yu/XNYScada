using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace XNY.Scada.WinForms.Mvvm;

/// <summary>表格列定义。Width 为 0 表示填充剩余宽度；Property 可用 "User.Name" 形式的嵌套路径（只读）</summary>
/// <param name="Header">标题。</param>
/// <param name="Property">属性。</param>
/// <param name="Width">宽度。</param>
/// <param name="Format">格式。</param>
public sealed record Col(string Header, string Property, int Width = 100, string? Format = null)
{
    /// <summary>是否可编辑。</summary>
    public bool Editable { get; init; }
    /// <summary>填充粗细。</summary>
    public float FillWeight { get; init; } = 100;
    /// <summary>可选值。</summary>
    public IList? Choices { get; init; }
    /// <summary>值成员。</summary>
    public string? ValueMember { get; init; }
    /// <summary>显示成员。</summary>
    public string? DisplayMember { get; init; }
    /// <summary>绑定的命令。</summary>
    public ICommand? Command { get; init; }

    /// <summary>填满。</summary>
    public static Col Fill(string header, string property, float weight = 100) => new(header, property, 0) { FillWeight = weight };

    /// <summary>编辑。</summary>
    public static Col Edit(string header, string property, int width = 100) => new(header, property, width) { Editable = true };

    /// <summary>选项。</summary>
    public static Col Choice(string header, string property, IList choices, int width = 100) =>
        new(header, property, width) { Editable = true, Choices = choices };

    /// <summary>键。</summary>
    public static Col Button(string text, ICommand command, int width = 60) => new(text, "", width) { Command = command };
}

/// <summary>绑定网格。</summary>
public sealed class BoundGrid<T> : DataGridView where T : class
{
    /// <summary>列表。</summary>
    private readonly ObservableBindingList<T> _list;
    /// <summary>路径列。</summary>
    private readonly Dictionary<int, string> _pathColumns = new();
    /// <summary>行颜色。</summary>
    private Func<T, Color?>? _rowColor;

    /// <summary>创建绑定网格。</summary>
    public BoundGrid(ObservableCollection<T> items, bool editable, params Col[] columns)
    {
        _list = new ObservableBindingList<T>(items);
        AutoGenerateColumns = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect = false;
        ReadOnly = !editable;
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        BackgroundColor = Color.White;
        BorderStyle = BorderStyle.None;
        Dock = DockStyle.Fill;
        EnableHeadersVisualStyles = false;
        ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(0xF0, 0xF3, 0xF7);
        ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        RowTemplate.Height = 26;
        AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(0xFA, 0xFB, 0xFD);
        DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xD6, 0xE4, 0xFF);
        DefaultCellStyle.SelectionForeColor = Color.Black;
        DoubleBuffered = true;

        foreach (var c in columns) Columns.Add(CreateColumn(c, editable));
        DataSource = _list;

        CellFormatting += OnCellFormatting;
        CellContentClick += OnCellContentClick;
        CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (IsCurrentCellDirty && CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell) CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        DataError += (_, e) =>
        {
            e.ThrowException = false;
            if (e.RowIndex >= 0 && e.Context.HasFlag(DataGridViewDataErrorContexts.Commit)) Rows[e.RowIndex].ErrorText = "输入格式错误：" + e.Exception?.Message;
        };
        CellEndEdit += (_, e) => Rows[e.RowIndex].ErrorText = "";
        _list.Synced += (_, _) =>
        {
            if (_selectedBinding == null) ClearSelection();
        };
    }

    /// <summary>当前值。</summary>
    public T? Current => CurrentRow?.DataBoundItem as T;

    /// <summary>选中绑定。</summary>
    private Delegate? _selectedBinding;

    /// <summary>创建列。</summary>
    private DataGridViewColumn CreateColumn(Col c, bool editable)
    {
        DataGridViewColumn col;
        var propType = c.Property.Contains('.') || c.Property.Length == 0 ? null : typeof(T).GetProperty(c.Property)?.PropertyType;
        if (c.Command != null)
        {
            col = new DataGridViewButtonColumn { Text = c.Header, UseColumnTextForButtonValue = true, HeaderText = "" };
        }
        else if (c.Choices != null)
        {
            var combo = new DataGridViewComboBoxColumn
            {
                DataSource = c.Choices,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat
            };
            if (c.ValueMember != null) combo.ValueMember = c.ValueMember;
            if (c.DisplayMember != null) combo.DisplayMember = c.DisplayMember;
            if (propType != null) combo.ValueType = propType;
            col = combo;
        }
        else if (propType == typeof(bool) || propType == typeof(bool?))
        {
            col = new DataGridViewCheckBoxColumn();
        }
        else
        {
            col = new DataGridViewTextBoxColumn();
        }

        col.HeaderText = c.Command != null ? "" : c.Header;
        col.SortMode = DataGridViewColumnSortMode.NotSortable;
        if (c.Property.Contains('.')) _pathColumns[Columns.Count] = c.Property;
        else if (c.Property.Length > 0) col.DataPropertyName = c.Property;
        col.ReadOnly = c.Command == null && !(editable && c.Editable);
        if (c.Format != null) col.DefaultCellStyle.Format = c.Format;
        if (c.Width > 0)
        {
            col.Width = c.Width;
        }
        else
        {
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col.FillWeight = c.FillWeight;
            col.MinimumWidth = 60;
        }
        if (c.Command != null) col.Tag = c.Command;
        return col;
    }

    /// <summary>处理单元格格式。</summary>
    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _list.Count) return;
        if (_pathColumns.TryGetValue(e.ColumnIndex, out var path))
        {
            var v = Bind.GetPath(_list[e.RowIndex], path);
            var fmt = Columns[e.ColumnIndex].DefaultCellStyle.Format;
            e.Value = v is bool ? v : Bind.Format(v, string.IsNullOrEmpty(fmt) ? null : fmt);
            e.FormattingApplied = true;
        }
        if (_rowColor?.Invoke(_list[e.RowIndex]) is { } color && e.CellStyle != null) e.CellStyle.ForeColor = color;
    }

    /// <summary>处理单元格内容击。</summary>
    private void OnCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _list.Count || Columns[e.ColumnIndex].Tag is not ICommand cmd) return;
        Bind.Execute(cmd, _list[e.RowIndex]);
    }

    /// <summary>按行着色（如报警级别、执行状态）</summary>
    public BoundGrid<T> RowColor(Func<T, Color?> color)
    {
        _rowColor = color;
        return this;
    }

    /// <summary>选中行与 ViewModel 属性双向绑定；数据刷新引起的选中变化不会写回</summary>
    public BoundGrid<T> Selected(INotifyPropertyChanged source, string property)
    {
        var pi = source.GetType().GetProperty(property)!;
        var updating = false;

        void Select()
        {
            if (updating) return;
            updating = true;
            try
            {
                var idx = pi.GetValue(source) is T item ? _list.IndexOf(item) : -1;
                if (idx < 0)
                {
                    ClearSelection();
                    if (CurrentCell != null && !IsCurrentCellInEditMode) CurrentCell = null;
                }
                else if (CurrentRow?.Index != idx)
                {
                    var firstVisible = Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                    if (firstVisible != null) CurrentCell = Rows[idx].Cells[firstVisible.Index];
                    Rows[idx].Selected = true;
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                updating = false;
            }
        }

        SelectionChanged += (_, _) =>
        {
            if (updating || _list.Syncing) return;
            var item = SelectedRows.Count > 0 ? SelectedRows[0].DataBoundItem as T : null;
            if (ReferenceEquals(item, pi.GetValue(source))) return;
            updating = true;
            try
            {
                pi.SetValue(source, item);
            }
            finally
            {
                updating = false;
            }
        };
        _list.Synced += (_, _) => Select();
        Action select = Select;
        _selectedBinding = select;
        Bind.Observe(source, property, Select, invokeNow: false);
        HandleCreated += (_, _) => BeginInvoke(Select);
        return this;
    }
}
