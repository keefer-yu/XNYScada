namespace XNY.Scada.Device;

/// <summary>PLC 驱动。实现类需标注 <see cref="PlcDriverAttribute"/>，参数通过 <see cref="DriverOptionAttribute"/> 声明。</summary>
public interface IPlcDriver : IAsyncDisposable
{
    string DriverKey { get; }

    bool IsConnected { get; }

    /// <summary>创建后、连接前调用，传入设备配置的参数</summary>
    void Configure(IReadOnlyDictionary<string, string> options);

    Task<DeviceResult> ConnectAsync(CancellationToken ct);

    Task DisconnectAsync();

    Task<DeviceResult<object?>> ReadAsync(TagDefinition tag, CancellationToken ct);

    Task<DeviceResult<object?[]>> ReadBatchAsync(IReadOnlyList<TagDefinition> tags, CancellationToken ct);

    Task<DeviceResult> WriteAsync(TagDefinition tag, object? value, CancellationToken ct);
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PlcDriverAttribute(string key) : Attribute
{
    public string Key { get; } = key;

    public string DisplayName { get; set; } = key;

    public string Vendor { get; set; } = "";

    public string AddressHelp { get; set; } = "";
}

public enum DriverOptionType
{
    String,
    Int,
    Bool,
    Enum
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DriverOptionAttribute(string key, string displayName) : Attribute
{
    public string Key { get; } = key;

    public string DisplayName { get; } = displayName;

    public DriverOptionType Type { get; set; } = DriverOptionType.String;

    public string Default { get; set; } = "";

    /// <summary>Enum 类型的可选值，逗号分隔</summary>
    public string Choices { get; set; } = "";

    public int Order { get; set; }
}

public sealed record DriverOptionSchema(string Key, string DisplayName, DriverOptionType Type, string Default, IReadOnlyList<string> Choices, int Order);

public sealed record DriverDescriptor(string Key, string DisplayName, string Vendor, string AddressHelp, Type DriverType, IReadOnlyList<DriverOptionSchema> Options);
