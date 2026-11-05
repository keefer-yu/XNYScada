using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using XNY.Scada.Presentation.Services;

namespace XNY.Scada.WinForms.Mvvm;

/// <summary>
/// 轻量 MVVM 绑定：WinForms 自带 Binding 不支持命令、集合同步与选中项双向绑定，这里统一按属性名反射实现。
/// 返回的 IDisposable 用于动态生成的控件（如节点属性面板）在重建时解除对 ViewModel 的订阅。
/// </summary>
public static class Bind
{
    /// <summary>调度器。</summary>
    public static IUiDispatcher Dispatcher { get; set; } = new ImmediateDispatcher();

    /// <summary>处理界面。</summary>
    private static void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    /// <summary>订阅。</summary>
    /// <param name="dispose">释放。</param>
    private sealed class Subscription(Action dispose) : IDisposable
    {
        /// <summary>释放。</summary>
        private Action? _dispose = dispose;

        /// <summary>释放资源。</summary>
        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }

    /// <summary>观察。</summary>
    public static IDisposable Observe(INotifyPropertyChanged source, string property, Action action, bool invokeNow = true)
    {
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == property) OnUi(action);
        };
        source.PropertyChanged += handler;
        if (invokeNow) action();
        return new Subscription(() => source.PropertyChanged -= handler);
    }

    /// <summary>观察。</summary>
    public static IDisposable Observe(INotifyCollectionChanged source, Action action, bool invokeNow = true)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => OnUi(action);
        source.CollectionChanged += handler;
        if (invokeNow) action();
        return new Subscription(() => source.CollectionChanged -= handler);
    }

    // ---------------- 属性反射与类型转换 ----------------

    /// <summary>属性。</summary>
    private static PropertyInfo Prop(object source, string property) =>
        source.GetType().GetProperty(property) ?? throw new ArgumentException($"{source.GetType().Name} 没有属性 {property}");

    /// <summary>访问器。</summary>
    /// <param name="Type">类型。</param>
    /// <param name="Get">获取。</param>
    /// <param name="Set">集合。</param>
    private sealed record Accessor(Type Type, Func<object?> Get, Action<object?> Set);

    /// <summary>访问方向。</summary>
    private static Accessor Access(object source, string property)
    {
        if (source is EntityProxy proxy)
        {
            var target = Prop(proxy.Target, property);
            return new Accessor(target.PropertyType, () => target.GetValue(proxy.Target), v =>
            {
                target.SetValue(proxy.Target, v);
                proxy.Raise(property);
            });
        }
        var pi = Prop(source, property);
        return new Accessor(pi.PropertyType, () => pi.GetValue(source), v => pi.SetValue(source, v));
    }

    /// <summary>获取路径。</summary>
    public static object? GetPath(object? obj, string path)
    {
        foreach (var part in path.Split('.'))
        {
            if (obj == null) return null;
            obj = obj.GetType().GetProperty(part)?.GetValue(obj);
        }
        return obj;
    }

    /// <summary>格式化。</summary>
    public static string Format(object? value, string? format = null) => value switch
    {
        null => "",
        DateTime d => d.ToString(format ?? "yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
        IFormattable f when format != null => f.ToString(format, CultureInfo.CurrentCulture),
        double d => d.ToString("0.####", CultureInfo.CurrentCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>尝试转换。</summary>
    public static bool TryConvert(string text, Type type, out object? value)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null && string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }
        var t = underlying ?? type;
        try
        {
            value = t == typeof(string) ? text
                : t.IsEnum ? Enum.Parse(t, text, true)
                : t == typeof(bool) ? bool.Parse(text)
                : Convert.ChangeType(text.Trim(), t, CultureInfo.CurrentCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidCastException)
        {
            value = null;
            return false;
        }
    }

    // ---------------- 单值绑定 ----------------

    /// <summary>只读显示：属性变化时刷新 Text</summary>
    public static IDisposable Text(Control control, INotifyPropertyChanged source, string property, Func<object?, string>? format = null)
    {
        var pi = Access(source, property);
        return Observe(source, property, () => control.Text = format != null ? format(pi.Get()) : Format(pi.Get()));
    }

    /// <summary>文本。</summary>
    public static IDisposable Text(ToolStripItem item, INotifyPropertyChanged source, string property)
    {
        var pi = Access(source, property);
        return Observe(source, property, () => item.Text = Format(pi.Get()));
    }

    /// <summary>TextBox 双向绑定，输入即写回（类型转换失败时不写回并标红）</summary>
    public static IDisposable TextBox(TextBoxBase box, INotifyPropertyChanged source, string property)
    {
        var pi = Access(source, property);
        var updating = false;
        var normalColor = box.BackColor;
        EventHandler changed = (_, _) =>
        {
            if (updating) return;
            if (TryConvert(box.Text, pi.Type, out var v))
            {
                box.BackColor = normalColor;
                updating = true;
                try
                {
                    if (!Equals(pi.Get(), v)) pi.Set(v);
                }
                finally
                {
                    updating = false;
                }
            }
            else
            {
                box.BackColor = Color.MistyRose;
            }
        };
        box.TextChanged += changed;
        var sub = Observe(source, property, () =>
        {
            if (updating) return;
            var text = Format(pi.Get());
            if (box is TextBox { Multiline: true }) text = text.ReplaceLineEndings("\r\n");
            if (box.Text.ReplaceLineEndings("\n") == text.ReplaceLineEndings("\n")) return;
            updating = true;
            box.Text = text;
            box.BackColor = normalColor;
            updating = false;
        });
        return new Subscription(() =>
        {
            sub.Dispose();
            box.TextChanged -= changed;
        });
    }

    /// <summary>CheckBox 双向绑定，支持 bool 与 "true"/"false" 字符串属性</summary>
    public static IDisposable Checked(CheckBox check, INotifyPropertyChanged source, string property)
    {
        var pi = Access(source, property);
        var updating = false;
        EventHandler changed = (_, _) =>
        {
            if (updating) return;
            updating = true;
            pi.Set(pi.Type == typeof(string) ? (check.Checked ? "true" : "false") : check.Checked);
            updating = false;
        };
        check.CheckedChanged += changed;
        var sub = Observe(source, property, () =>
        {
            if (updating) return;
            var v = pi.Get();
            updating = true;
            check.Checked = v is bool b ? b : bool.TryParse(v as string, out var sb) && sb;
            updating = false;
        });
        return new Subscription(() =>
        {
            sub.Dispose();
            check.CheckedChanged -= changed;
        });
    }

    /// <summary>日期。</summary>
    public static IDisposable Date(DateTimePicker picker, INotifyPropertyChanged source, string property)
    {
        var pi = Access(source, property);
        var updating = false;
        picker.ValueChanged += (_, _) =>
        {
            if (updating) return;
            updating = true;
            pi.Set(picker.Value.Date);
            updating = false;
        };
        return Observe(source, property, () =>
        {
            if (updating || pi.Get() is not DateTime d) return;
            updating = true;
            picker.Value = d < picker.MinDate ? picker.MinDate : d;
            updating = false;
        });
    }

    /// <summary>编号。</summary>
    public static IDisposable Number(NumericUpDown number, INotifyPropertyChanged source, string property)
    {
        var pi = Access(source, property);
        var updating = false;
        number.ValueChanged += (_, _) =>
        {
            if (updating) return;
            updating = true;
            pi.Set(Convert.ChangeType(number.Value, Nullable.GetUnderlyingType(pi.Type) ?? pi.Type, CultureInfo.InvariantCulture));
            updating = false;
        };
        return Observe(source, property, () =>
        {
            if (updating) return;
            updating = true;
            var v = Convert.ToDecimal(pi.Get() ?? 0, CultureInfo.InvariantCulture);
            number.Value = Math.Clamp(v, number.Minimum, number.Maximum);
            updating = false;
        });
    }

    /// <summary>可见。</summary>
    public static IDisposable Visible(Control control, INotifyPropertyChanged source, string property, bool invert = false)
    {
        var pi = Access(source, property);
        return Observe(source, property, () =>
        {
            var v = pi.Get();
            var visible = v is bool b ? b : v != null;
            control.Visible = invert ? !visible : visible;
        });
    }

    /// <summary>是否启用。</summary>
    public static IDisposable Enabled(Control control, INotifyPropertyChanged source, string property)
    {
        var pi = Access(source, property);
        return Observe(source, property, () => control.Enabled = pi.Get() is true);
    }

    // ---------------- ComboBox ----------------

    /// <summary>下拉框项。</summary>
    /// <param name="Value">值。</param>
    /// <param name="Text">文本。</param>
    public sealed record ComboItem(object? Value, string Text)
    {
        /// <summary>转换为字符串。</summary>
        public override string ToString() => Text;
    }

    /// <summary>
    /// 下拉选择：items 为候选（可为 ObservableCollection，变化时自动刷新）；
    /// valueOf 从候选取写入属性的值（默认候选本身），display 为显示文本
    /// </summary>
    public static IDisposable Combo(ComboBox combo, IEnumerable items, INotifyPropertyChanged source, string property,
        Func<object, string>? display = null, Func<object, object?>? valueOf = null)
    {
        var pi = Access(source, property);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        var updating = false;

        void Select()
        {
            var current = pi.Get();
            updating = true;
            combo.SelectedIndex = combo.Items.Cast<ComboItem>().ToList().FindIndex(i => Equals(i.Value, current));
            updating = false;
        }

        void Fill()
        {
            updating = true;
            combo.BeginUpdate();
            combo.Items.Clear();
            foreach (var i in items)
            {
                if (i == null) continue;
                combo.Items.Add(new ComboItem(valueOf != null ? valueOf(i) : i, display != null ? display(i) : Format(i)));
            }
            combo.EndUpdate();
            updating = false;
            Select();
        }

        Fill();
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (updating || combo.SelectedItem is not ComboItem item) return;
            updating = true;
            pi.Set(item.Value);
            updating = false;
        };
        var subs = new List<IDisposable> { Observe(source, property, Select, invokeNow: false) };
        if (items is INotifyCollectionChanged ncc) subs.Add(Observe(ncc, Fill, invokeNow: false));
        return new Subscription(() => subs.ForEach(s => s.Dispose()));
    }

    /// <summary>可输入下拉：文本双向绑定 + 候选列表（点位名、流程编码等）</summary>
    public static IDisposable EditableCombo(ComboBox combo, IEnumerable<string> suggestions, INotifyPropertyChanged source, string property)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDown;
        combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        combo.AutoCompleteSource = AutoCompleteSource.ListItems;
        combo.Items.AddRange(suggestions.Cast<object>().ToArray());
        var pi = Access(source, property);
        var updating = false;
        EventHandler changed = (_, _) =>
        {
            if (updating) return;
            updating = true;
            pi.Set(combo.Text);
            updating = false;
        };
        combo.TextChanged += changed;
        combo.SelectedIndexChanged += changed;
        var sub = Observe(source, property, () =>
        {
            if (updating) return;
            updating = true;
            combo.Text = Format(pi.Get());
            updating = false;
        });
        return new Subscription(() =>
        {
            sub.Dispose();
            combo.TextChanged -= changed;
            combo.SelectedIndexChanged -= changed;
        });
    }

    // ---------------- 命令 ----------------

    /// <summary>按钮绑定命令：点击执行，Enabled 跟随 CanExecute</summary>
    public static IDisposable Command(ButtonBase button, ICommand command, Func<object?>? parameter = null)
    {
        void Update() => button.Enabled = command.CanExecute(parameter?.Invoke());
        EventHandler canChanged = (_, _) => OnUi(Update);
        command.CanExecuteChanged += canChanged;
        button.Click += (_, _) =>
        {
            var p = parameter?.Invoke();
            if (command.CanExecute(p)) command.Execute(p);
        };
        Update();
        return new Subscription(() => command.CanExecuteChanged -= canChanged);
    }

    /// <summary>执行。</summary>
    public static void Execute(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter)) command.Execute(parameter);
    }

    // ---------------- 列表 ----------------

    /// <summary>ListBox 显示集合，选中项双向绑定</summary>
    public static void List<T>(ListBox list, ObservableCollection<T> items, Func<T, string> display,
        INotifyPropertyChanged? source = null, string? selectedProperty = null) where T : class
    {
        var updating = false;
        var pi = source != null && selectedProperty != null ? Access(source, selectedProperty) : null;

        void Select()
        {
            if (pi == null) return;
            updating = true;
            list.SelectedIndex = items.IndexOf((pi.Get() as T)!);
            updating = false;
        }

        void Fill()
        {
            updating = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var i in items) list.Items.Add(new ComboItem(i, display(i)));
            list.EndUpdate();
            updating = false;
            Select();
        }

        list.SelectedIndexChanged += (_, _) =>
        {
            if (updating || pi == null) return;
            updating = true;
            pi.Set((list.SelectedItem as ComboItem)?.Value);
            updating = false;
        };
        Observe(items, Fill);
        if (pi != null) Observe(source!, selectedProperty!, Select, invokeNow: false);
    }
}

/// <summary>把 ObservableCollection 同步为 BindingList，供 DataGridView 数据绑定（元素引用相同，单元格编辑直接作用于 ViewModel 对象）</summary>
public sealed class ObservableBindingList<T> : BindingList<T>
{
    /// <summary>源。</summary>
    private readonly ObservableCollection<T> _source;

    /// <summary>创建可观察绑定列表。</summary>
    public ObservableBindingList(ObservableCollection<T> source) : base(new List<T>(source))
    {
        _source = source;
        AllowNew = false;
        source.CollectionChanged += (_, e) =>
        {
            if (Bind.Dispatcher.CheckAccess()) Sync(e);
            else Bind.Dispatcher.Invoke(() => Sync(e));
        };
    }

    /// <summary>同步过程中为 true，表格据此忽略因数据刷新导致的选中变化</summary>
    public bool Syncing { get; private set; }

    /// <summary>已同步发生变化时触发。</summary>
    public event EventHandler? Synced;

    /// <summary>同步。</summary>
    private void Sync(NotifyCollectionChangedEventArgs e)
    {
        Syncing = true;
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewStartingIndex >= 0 && e.NewStartingIndex <= Count:
                    for (var i = 0; i < e.NewItems!.Count; i++) Insert(e.NewStartingIndex + i, (T)e.NewItems[i]!);
                    break;
                case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0:
                    for (var i = 0; i < e.OldItems!.Count && e.OldStartingIndex < Count; i++) RemoveAt(e.OldStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Replace when e.OldStartingIndex >= 0 && e.OldStartingIndex < Count:
                    this[e.OldStartingIndex] = (T)e.NewItems![0]!;
                    break;
                default:
                    RaiseListChangedEvents = false;
                    ClearItems();
                    foreach (var i in _source) Add(i);
                    RaiseListChangedEvents = true;
                    ResetBindings();
                    break;
            }
            if (Count != _source.Count)
            {
                RaiseListChangedEvents = false;
                ClearItems();
                foreach (var i in _source) Add(i);
                RaiseListChangedEvents = true;
                ResetBindings();
            }
        }
        finally
        {
            Syncing = false;
        }
        Synced?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>为无属性通知的实体（如 MES 接口、角色）提供可绑定包装：通过本包装写入的属性会触发通知</summary>
/// <param name="target">目标。</param>
public sealed class EntityProxy(object target) : INotifyPropertyChanged
{
    /// <summary>操作对象。</summary>
    public object Target { get; } = target;

    /// <summary>属性发生变化时触发。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>引发。</summary>
    public void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
