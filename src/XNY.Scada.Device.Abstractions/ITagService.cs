namespace XNY.Scada.Device;

/// <summary>按点位全名（设备名.点位名）读写，流程引擎和界面统一使用此接口</summary>
public interface ITagService
{
    event EventHandler<TagChangedEventArgs>? TagChanged;

    TagDefinition? FindTag(string fullName);

    /// <summary>读取缓存值（由轮询刷新）</summary>
    TagValue? GetValue(string fullName);

    IReadOnlyList<TagSnapshot> GetAll();

    /// <summary>直接从设备读取并刷新缓存</summary>
    Task<DeviceResult<object?>> ReadAsync(string fullName, CancellationToken ct = default);

    Task<DeviceResult> WriteAsync(string fullName, object? value, CancellationToken ct = default);
}

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

public interface IDeviceManager
{
    event EventHandler<DeviceStatusInfo>? StatusChanged;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync();

    /// <summary>重新加载设备与点位配置</summary>
    Task ReloadAsync(CancellationToken ct = default);

    IReadOnlyList<DeviceStatusInfo> GetStatuses();

    Task<DeviceResult> TestConnectionAsync(string driverKey, IReadOnlyDictionary<string, string> options, CancellationToken ct = default);
}

public interface IDriverRegistry
{
    IReadOnlyList<DriverDescriptor> Drivers { get; }

    DriverDescriptor? Find(string key);

    IPlcDriver Create(string key, IReadOnlyDictionary<string, string> options);
}
