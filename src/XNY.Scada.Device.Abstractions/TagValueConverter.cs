using System.Globalization;
using XNY.Scada.Domain;

namespace XNY.Scada.Device;

public static class TagValueConverter
{
    public static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
        IConvertible c => System.Convert.ToDouble(c, CultureInfo.InvariantCulture) != 0,
        _ => false
    };

    /// <summary>把任意值转换为点位原始数据类型（写入前使用，会反算缩放）</summary>
    public static object ToRaw(TagDefinition tag, object? value)
    {
        if (tag.DataType == TagDataType.Bool) return ToBool(value);
        if (tag.DataType == TagDataType.String) return System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        var d = System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
        if (tag.HasScaling) d = (d - tag.Offset) / tag.Scale;
        return tag.DataType switch
        {
            TagDataType.Int16 => (object)(short)Math.Round(d),
            TagDataType.UInt16 => (ushort)Math.Round(d),
            TagDataType.Int32 => (int)Math.Round(d),
            TagDataType.UInt32 => (uint)Math.Round(d),
            TagDataType.Int64 => (long)Math.Round(d),
            TagDataType.Float => (float)d,
            TagDataType.Double => d,
            _ => d
        };
    }

    /// <summary>原始值转工程值（应用缩放）</summary>
    public static object? ToEngineering(TagDefinition tag, object? raw)
    {
        if (raw == null || !tag.HasScaling) return raw;
        var d = System.Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        return d * tag.Scale + tag.Offset;
    }

    public static object? Parse(TagDataType type, string? text)
    {
        if (text == null) return null;
        var c = CultureInfo.InvariantCulture;
        return type switch
        {
            TagDataType.Bool => ToBool(text),
            TagDataType.Int16 => short.Parse(text, c),
            TagDataType.UInt16 => ushort.Parse(text, c),
            TagDataType.Int32 => int.Parse(text, c),
            TagDataType.UInt32 => uint.Parse(text, c),
            TagDataType.Int64 => long.Parse(text, c),
            TagDataType.Float => float.Parse(text, c),
            TagDataType.Double => double.Parse(text, c),
            _ => text
        };
    }
}
