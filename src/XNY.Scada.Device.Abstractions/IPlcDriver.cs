namespace XNY.Scada.Device;

/// <summary>PLC 驱动。实现类需标注 <see cref="PlcDriverAttribute"/>，参数通过 <see cref="DriverOptionAttribute"/> 声明。</summary>
public interface IPlcDriver : IAsyncDisposable
{
    /// <summary>驱动键。</summary>
    string DriverKey { get; }

    /// <summary>是否连接。</summary>
    bool IsConnected { get; }

    /// <summary>创建后、连接前调用，传入设备配置的参数</summary>
    void Configure(IReadOnlyDictionary<string, string> options);

    /// <summary>连接。</summary>
    Task<DeviceResult> ConnectAsync(CancellationToken ct);

    /// <summary>断开。</summary>
    Task DisconnectAsync();

    /// <summary>读取。</summary>
    Task<DeviceResult<object?>> ReadAsync(TagDefinition tag, CancellationToken ct);

    /// <summary>读取批量。</summary>
    Task<DeviceResult<object?[]>> ReadBatchAsync(IReadOnlyList<TagDefinition> tags, CancellationToken ct);

    /// <summary>写入。</summary>
    Task<DeviceResult> WriteAsync(TagDefinition tag, object? value, CancellationToken ct);
}

/// <summary>标记PLC驱动的特性。</summary>
/// <param name="key">键。</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PlcDriverAttribute(string key) : Attribute
{
    /// <summary>PLC驱动键。</summary>
    public string Key { get; } = key;

    /// <summary>显示名。</summary>
    public string DisplayName { get; set; } = key;

    /// <summary>厂商。</summary>
    public string Vendor { get; set; } = "";

    /// <summary>地址填写说明。</summary>
    public string AddressHelp { get; set; } = "";
}

/// <summary>驱动选项类型。</summary>
public enum DriverOptionType
{
    /// <summary>字符串。</summary>
    String,
    /// <summary>整数。</summary>
    Int,
    /// <summary>布尔。</summary>
    Bool,
    /// <summary>枚举。</summary>
    Enum
}

/// <summary>标记驱动选项的特性。</summary>
/// <param name="key">键。</param>
/// <param name="displayName">显示名称。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DriverOptionAttribute(string key, string displayName) : Attribute
{
    /// <summary>驱动选项键。</summary>
    public string Key { get; } = key;

    /// <summary>显示名。</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>驱动选项类型。</summary>
    public DriverOptionType Type { get; set; } = DriverOptionType.String;

    /// <summary>默认。</summary>
    public string Default { get; set; } = "";

    /// <summary>Enum 类型的可选值，逗号分隔</summary>
    public string Choices { get; set; } = "";

    /// <summary>显示顺序。</summary>
    public int Order { get; set; }
}

/// <summary>驱动选项结构。</summary>
/// <param name="Key">键。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Type">类型。</param>
/// <param name="Default">默认。</param>
/// <param name="Choices">可选值。</param>
/// <param name="Order">显示顺序。</param>
public sealed record DriverOptionSchema(string Key, string DisplayName, DriverOptionType Type, string Default, IReadOnlyList<string> Choices, int Order);

/// <summary>驱动描述。</summary>
/// <param name="Key">键。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Vendor">厂商。</param>
/// <param name="AddressHelp">地址填写说明。</param>
/// <param name="DriverType">驱动类型。</param>
/// <param name="Options">选项。</param>
public sealed record DriverDescriptor(string Key, string DisplayName, string Vendor, string AddressHelp, Type DriverType, IReadOnlyList<DriverOptionSchema> Options);
