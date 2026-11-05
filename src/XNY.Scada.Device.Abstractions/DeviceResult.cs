namespace XNY.Scada.Device;

public class DeviceResult
{
    public bool IsSuccess { get; init; }

    public string Message { get; init; } = "";

    public static DeviceResult Ok() => new() { IsSuccess = true };

    public static DeviceResult Fail(string message) => new() { IsSuccess = false, Message = message };

    public override string ToString() => IsSuccess ? "OK" : Message;
}

public sealed class DeviceResult<T> : DeviceResult
{
    public T? Value { get; init; }

    public static DeviceResult<T> Ok(T value) => new() { IsSuccess = true, Value = value };

    public static new DeviceResult<T> Fail(string message) => new() { IsSuccess = false, Message = message };
}
