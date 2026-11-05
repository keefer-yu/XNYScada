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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>非 null / 非空字符串 / 非空集合 → Visible；参数 "Invert" 反转</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>字符串属性值 ↔ CheckBox</summary>
public sealed class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "true" : "false";
}

/// <summary>SVG 风格路径字符串 → Geometry</summary>
public sealed class PathDataConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } s ? Geometry.Parse(s) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>状态 → 画刷：bool（在线/离线）、AlarmLevel、NodeExecState、FlowInstanceStatus</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public static readonly Brush Good = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x5C)));
    public static readonly Brush Bad = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E)));
    public static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x20)));
    public static readonly Brush Info = Freeze(new SolidColorBrush(Color.FromRgb(0x32, 0x6C, 0xE5)));
    public static readonly Brush Idle = Freeze(new SolidColorBrush(Color.FromRgb(0x90, 0x99, 0xA8)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>节点分类 → 标题栏颜色</summary>
public sealed class CategoryBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> Map = new()
    {
        ["逻辑"] = Make(0x5B, 0x6B, 0x8C),
        ["通讯"] = Make(0x1F, 0x7A, 0xC8),
        ["业务"] = Make(0x2E, 0x9E, 0x6A),
        ["高级"] = Make(0x9B, 0x4D, 0xCA)
    };

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Map.TryGetValue(s, out var b) ? b : Make(0x70, 0x70, 0x70);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>DataGrid 下拉列使用的枚举列表</summary>
public static class EnumLists
{
    public static Array TagDataTypes { get; } = Enum.GetValues(typeof(TagDataType));
    public static Array TagAccessModes { get; } = Enum.GetValues(typeof(TagAccess));
    public static Array AlarmKinds { get; } = Enum.GetValues(typeof(AlarmKind));
    public static Array AlarmLevels { get; } = Enum.GetValues(typeof(AlarmLevel));
    public static Array TriggerTypes { get; } = Enum.GetValues(typeof(TriggerType));
    public static Array VariableScopes { get; } = Enum.GetValues(typeof(VariableScope));
    public static string[] HttpMethods { get; } = ["POST", "GET", "PUT", "SOAP"];
}
