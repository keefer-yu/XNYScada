namespace XNY.Scada.Device;

/// <summary>设备结果。</summary>
public class DeviceResult
{
    /// <summary>是否是否成功。</summary>
    public bool IsSuccess { get; init; }

    /// <summary>消息。</summary>
    public string Message { get; init; } = "";

    /// <summary>OK。</summary>
    public static DeviceResult Ok() => new() { IsSuccess = true };

    /// <summary>失败。</summary>
    public static DeviceResult Fail(string message) => new() { IsSuccess = false, Message = message };

    /// <summary>转换为字符串。</summary>
    public override string ToString() => IsSuccess ? "OK" : Message;
}

/// <summary>设备结果。</summary>
public sealed class DeviceResult<T> : DeviceResult
{
    /// <summary>设备结果值。</summary>
    public T? Value { get; init; }

    /// <summary>OK。</summary>
    public static DeviceResult<T> Ok(T value) => new() { IsSuccess = true, Value = value };

    /// <summary>失败。</summary>
    public static new DeviceResult<T> Fail(string message) => new() { IsSuccess = false, Message = message };
}
