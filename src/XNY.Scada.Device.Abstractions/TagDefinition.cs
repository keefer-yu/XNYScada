using XNY.Scada.Domain;

namespace XNY.Scada.Device;

/// <summary>标记定义。</summary>
public sealed record TagDefinition
{
    /// <summary>标记定义标识。</summary>
    public long Id { get; init; }

    /// <summary>设备名称。</summary>
    public string DeviceName { get; init; } = "";

    /// <summary>标记定义名称。</summary>
    public string Name { get; init; } = "";

    /// <summary>PLC 地址。</summary>
    public string Address { get; init; } = "";

    /// <summary>数据类型。</summary>
    public TagDataType DataType { get; init; }

    /// <summary>长度。</summary>
    public int Length { get; init; }

    /// <summary>缩放系数。</summary>
    public double Scale { get; init; } = 1;

    /// <summary>偏移量。</summary>
    public double Offset { get; init; }

    /// <summary>访问方向。</summary>
    public TagAccess Access { get; init; } = TagAccess.ReadWrite;

    /// <summary>点位全名。</summary>
    public string FullName => $"{DeviceName}.{Name}";

    /// <summary>是否缩放。</summary>
    public bool HasScaling => DataType is not (TagDataType.Bool or TagDataType.String) && (Scale != 1 || Offset != 0);
}

/// <summary>标记质量。</summary>
public enum TagQuality
{
    /// <summary>良品。</summary>
    Good,
    /// <summary>不良。</summary>
    Bad,
    /// <summary>不确定。</summary>
    Uncertain
}

/// <summary>标记值。</summary>
/// <param name="Value">值。</param>
/// <param name="Quality">值的质量。</param>
/// <param name="Timestamp">时间戳。</param>
public sealed record TagValue(object? Value, TagQuality Quality, DateTime Timestamp)
{
    /// <summary>不良。</summary>
    public static TagValue Bad() => new(null, TagQuality.Bad, DateTime.Now);
}

/// <summary>标记已变化事件参数。</summary>
/// <param name="tag">标记。</param>
/// <param name="oldValue">旧值。</param>
/// <param name="newValue">新值。</param>
public sealed class TagChangedEventArgs(TagDefinition tag, TagValue? oldValue, TagValue newValue) : EventArgs
{
    /// <summary>标记。</summary>
    public TagDefinition Tag { get; } = tag;
    /// <summary>修改前的值。</summary>
    public TagValue? OldValue { get; } = oldValue;
    /// <summary>修改后的值。</summary>
    public TagValue NewValue { get; } = newValue;

    /// <summary>是否上升沿边。</summary>
    public bool IsRisingEdge =>
        TagValueConverter.ToBool(NewValue.Value) && !TagValueConverter.ToBool(OldValue?.Value);

    /// <summary>是否下降沿边。</summary>
    public bool IsFallingEdge =>
        !TagValueConverter.ToBool(NewValue.Value) && TagValueConverter.ToBool(OldValue?.Value);
}

/// <summary>标记快照。</summary>
/// <param name="Tag">标记。</param>
/// <param name="Value">值。</param>
public sealed record TagSnapshot(TagDefinition Tag, TagValue Value);
