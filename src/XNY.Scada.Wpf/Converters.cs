using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using XNY.Scada.Domain;
using XNY.Scada.Flow.Model;
using XNY.Scada.Presentation.ViewModels;

namespace XNY.Scada.Wpf;

/// <summary>true → Visible；参数 "Invert" 反转</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>转换。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>转换返回。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>非 null / 非空字符串 / 非空集合 → Visible；参数 "Invert" 反转</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    /// <summary>转换。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value switch
        {
            null => false,
            string s => s.Length > 0,
            int i => i > 0,
            ICollection c => c.Count > 0,
            _ => true
        };
        if (parameter as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>转换返回。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>反向布尔转换器。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    /// <summary>转换。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    /// <summary>转换返回。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>字符串属性值 ↔ CheckBox</summary>
public sealed class StringToBoolConverter : IValueConverter
{
    /// <summary>转换。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

    /// <summary>转换返回。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "true" : "false";
}

/// <summary>SVG 风格路径字符串 → Geometry</summary>
public sealed class PathDataConverter : IValueConverter
{
    /// <summary>转换。</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } s ? Geometry.Parse(s) : null;

    /// <summary>转换返回。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>状态 → 画刷：bool（在线/离线）、AlarmLevel、NodeExecState、FlowInstanceStatus</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    /// <summary>良品。</summary>
    public static readonly Brush Good = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x5C)));
    /// <summary>不良。</summary>
    public static readonly Brush Bad = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E)));
    /// <summary>警告。</summary>
    public static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x20)));
    /// <summary>信息。</summary>
    public static readonly Brush Info = Freeze(new SolidColorBrush(Color.FromRgb(0x32, 0x6C, 0xE5)));
    /// <summary>空闲。</summary>
    public static readonly Brush Idle = Freeze(new SolidColorBrush(Color.FromRgb(0x90, 0x99, 0xA8)));

    /// <summary>冻结。</summary>
    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>转换。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        true => Good,
        false => Bad,
        AlarmLevel.Critical or AlarmLevel.Error => Bad,
        AlarmLevel.Warning => Warn,
        AlarmLevel.Info => Info,
        NodeExecState.Running => Info,
        NodeExecState.Done => Good,
        NodeExecState.Failed => Bad,
        FlowInstanceStatus.Completed => Good,
        FlowInstanceStatus.Running => Info,
        FlowInstanceStatus.Failed or FlowInstanceStatus.TimedOut => Bad,
        FlowInstanceStatus.Cancelled => Warn,
        _ => Idle
    };

    /// <summary>转换返回。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>节点分类 → 标题栏颜色</summary>
public sealed class CategoryBrushConverter : IValueConverter
{
    /// <summary>映射。</summary>
    private static readonly Dictionary<string, Brush> Map = new()
    {
        ["逻辑"] = Make(0x5B, 0x6B, 0x8C),
        ["通讯"] = Make(0x1F, 0x7A, 0xC8),
        ["业务"] = Make(0x2E, 0x9E, 0x6A),
        ["高级"] = Make(0x9B, 0x4D, 0xCA)
    };

    /// <summary>创建。</summary>
    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>转换。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Map.TryGetValue(s, out var b) ? b : Make(0x70, 0x70, 0x70);

    /// <summary>转换返回。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>DataGrid 下拉列使用的枚举列表</summary>
public static class EnumLists
{
    /// <summary>标记数据类型。</summary>
    public static Array TagDataTypes { get; } = Enum.GetValues(typeof(TagDataType));
    /// <summary>标记访问模式。</summary>
    public static Array TagAccessModes { get; } = Enum.GetValues(typeof(TagAccess));
    /// <summary>报警种类。</summary>
    public static Array AlarmKinds { get; } = Enum.GetValues(typeof(AlarmKind));
    /// <summary>报警级别。</summary>
    public static Array AlarmLevels { get; } = Enum.GetValues(typeof(AlarmLevel));
    /// <summary>触发器类型。</summary>
    public static Array TriggerTypes { get; } = Enum.GetValues(typeof(TriggerType));
    /// <summary>变量范围。</summary>
    public static Array VariableScopes { get; } = Enum.GetValues(typeof(VariableScope));
    /// <summary>HTTP方法。</summary>
    public static string[] HttpMethods { get; } = ["POST", "GET", "PUT", "SOAP"];
}
