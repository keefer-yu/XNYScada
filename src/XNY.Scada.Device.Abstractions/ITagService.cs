namespace XNY.Scada.Device;

/// <summary>按点位全名（设备名.点位名）读写，流程引擎和界面统一使用此接口</summary>
public interface ITagService
{
    /// <summary>标记发生变化时触发。</summary>
    event EventHandler<TagChangedEventArgs>? TagChanged;

    /// <summary>查找标记。</summary>
    TagDefinition? FindTag(string fullName);

    /// <summary>读取缓存值（由轮询刷新）</summary>
    TagValue? GetValue(string fullName);

    /// <summary>获取全部。</summary>
    IReadOnlyList<TagSnapshot> GetAll();

    /// <summary>直接从设备读取并刷新缓存</summary>
    Task<DeviceResult<object?>> ReadAsync(string fullName, CancellationToken ct = default);

    /// <summary>写入。</summary>
    Task<DeviceResult> WriteAsync(string fullName, object? value, CancellationToken ct = default);
}

/// <summary>设备状态信息。</summary>
/// <param name="DeviceName">设备名称。</param>
/// <param name="DriverKey">驱动键。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="Connected">是否已连接。</param>
/// <param name="ReadCount">读取次数。</param>
/// <param name="ErrorCount">错误次数。</param>
/// <param name="LastScanMs">最近一次扫描耗时（毫秒）。</param>
/// <param name="LastError">最近一次错误。</param>
/// <param name="LastConnectedAt">最近一次连接成功的时间。</param>
public sealed record DeviceStatusInfo(
    string DeviceName,
    string DriverKey,
    bool Enabled,
    bool Connected,
    long ReadCount,
    long ErrorCount,
    double LastScanMs,
    string? LastError,
    DateTime? LastConnectedAt);

/// <summary>设备管理器的契约。</summary>
public interface IDeviceManager
{
    /// <summary>状态发生变化时触发。</summary>
    event EventHandler<DeviceStatusInfo>? StatusChanged;

    /// <summary>是否正在运行。</summary>
    bool IsRunning { get; }

    /// <summary>启动。</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止。</summary>
    Task StopAsync();

    /// <summary>重新加载设备与点位配置</summary>
    Task ReloadAsync(CancellationToken ct = default);

    /// <summary>获取状态。</summary>
    IReadOnlyList<DeviceStatusInfo> GetStatuses();

    /// <summary>测试连接。</summary>
    Task<DeviceResult> TestConnectionAsync(string driverKey, IReadOnlyDictionary<string, string> options, CancellationToken ct = default);
}

/// <summary>驱动注册的契约。</summary>
public interface IDriverRegistry
{
    /// <summary>驱动。</summary>
    IReadOnlyList<DriverDescriptor> Drivers { get; }

    /// <summary>查找。</summary>
    DriverDescriptor? Find(string key);

    /// <summary>创建。</summary>
    IPlcDriver Create(string key, IReadOnlyDictionary<string, string> options);
}
