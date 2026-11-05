using System.Collections.Concurrent;
using XNY.Scada.Domain;

namespace XNY.Scada.Device.Runtime;

/// <summary>内存模拟 PLC，同一 Instance 名称的设备共享一块内存，便于调试流程和集成测试</summary>
public static class SimulatorMemory
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> Instances = new(StringComparer.OrdinalIgnoreCase);

    public static ConcurrentDictionary<string, object> Get(string instance) =>
        Instances.GetOrAdd(instance, _ => new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase));

    public static void Set(string instance, string address, object value) => Get(instance)[address] = value;

    public static object? Read(string instance, string address) => Get(instance).TryGetValue(address, out var v) ? v : null;

    public static void Clear(string instance) => Get(instance).Clear();
}

[PlcDriver("Simulator", DisplayName = "模拟 PLC", Vendor = "XNY", AddressHelp = "任意字符串地址，如 D100、M0")]
[DriverOption("Instance", "模拟实例名", Default = "default")]
[DriverOption("FailConnect", "模拟连接失败", Type = DriverOptionType.Bool, Default = "false")]
public sealed class SimulatorDriver : PlcDriverBase
{
    private bool _connected;

    public override string DriverKey => "Simulator";

    public override bool IsConnected => _connected;

    private string Instance => GetOption("Instance", "default");

    public override Task<DeviceResult> ConnectAsync(CancellationToken ct)
    {
        if (GetBool("FailConnect", false)) return Task.FromResult(DeviceResult.Fail("模拟连接失败"));
        _connected = true;
        return Task.FromResult(DeviceResult.Ok());
    }

    public override Task DisconnectAsync()
    {
        _connected = false;
        return Task.CompletedTask;
    }

    protected override DeviceResult<object?> ReadCore(TagDefinition tag)
    {
        var v = SimulatorMemory.Read(Instance, tag.Address);
        if (v == null) return DeviceResult<object?>.Ok(Default(tag.DataType));
        try
        {
            return DeviceResult<object?>.Ok(TagValueConverter.ToRaw(tag with { Scale = 1, Offset = 0 }, v));
        }
        catch (Exception ex)
        {
            return DeviceResult<object?>.Fail(ex.Message);
        }
    }

    protected override DeviceResult WriteCore(TagDefinition tag, object raw)
    {
        SimulatorMemory.Set(Instance, tag.Address, raw);
        return DeviceResult.Ok();
    }

    private static object Default(TagDataType t) => t switch
    {
        TagDataType.Bool => false,
        TagDataType.String => "",
        TagDataType.Float => 0f,
        TagDataType.Double => 0d,
        TagDataType.Int16 => (short)0,
        TagDataType.UInt16 => (ushort)0,
        TagDataType.UInt32 => 0u,
        TagDataType.Int64 => 0L,
        _ => 0
    };
}
