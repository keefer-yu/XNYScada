using XNY.Scada.Domain;

namespace XNY.Scada.Device;

public sealed record TagDefinition
{
    public long Id { get; init; }

    public string DeviceName { get; init; } = "";

    public string Name { get; init; } = "";

    public string Address { get; init; } = "";

    public TagDataType DataType { get; init; }

    public int Length { get; init; }

    public double Scale { get; init; } = 1;

    public double Offset { get; init; }

    public TagAccess Access { get; init; } = TagAccess.ReadWrite;

    public string FullName => $"{DeviceName}.{Name}";

    public bool HasScaling => DataType is not (TagDataType.Bool or TagDataType.String) && (Scale != 1 || Offset != 0);
}

public enum TagQuality
{
    Good,
    Bad,
    Uncertain
}

public sealed record TagValue(object? Value, TagQuality Quality, DateTime Timestamp)
{
    public static TagValue Bad() => new(null, TagQuality.Bad, DateTime.Now);
}

public sealed class TagChangedEventArgs(TagDefinition tag, TagValue? oldValue, TagValue newValue) : EventArgs
{
    public TagDefinition Tag { get; } = tag;
    public TagValue? OldValue { get; } = oldValue;
    public TagValue NewValue { get; } = newValue;

    public bool IsRisingEdge =>
        TagValueConverter.ToBool(NewValue.Value) && !TagValueConverter.ToBool(OldValue?.Value);

    public bool IsFallingEdge =>
        !TagValueConverter.ToBool(NewValue.Value) && TagValueConverter.ToBool(OldValue?.Value);
}

public sealed record TagSnapshot(TagDefinition Tag, TagValue Value);
