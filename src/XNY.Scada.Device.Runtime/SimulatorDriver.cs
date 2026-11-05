using System.Collections.Concurrent;
using XNY.Scada.Domain;

namespace XNY.Scada.Device.Runtime;

/// <summary>内存模拟 PLC，同一 Instance 名称的设备共享一块内存，便于调试流程和集成测试</summary>
public static class SimulatorMemory
{
    /// <summary>实例。</summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> Instances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>获取。</summary>
    public static ConcurrentDictionary<string, object> Get(string instance) =>
        Instances.GetOrAdd(instance, _ => new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase));

    /// <summary>设置。</summary>
    public static void Set(string instance, string address, object value) => Get(instance)[address] = value;

    /// <summary>读取。</summary>
    public static object? Read(string instance, string address) => Get(instance).TryGetValue(address, out var v) ? v : null;

    /// <summary>清空。</summary>
    public static void Clear(string instance) => Get(instance).Clear();
}

/// <summary>模拟驱动驱动。</summary>
[PlcDriver("Simulator", DisplayName = "模拟 PLC", Vendor = "XNY", AddressHelp = "任意字符串地址，如 D100、M0")]
[DriverOption("Instance", "模拟实例名", Default = "default")]
[DriverOption("FailConnect", "模拟连接失败", Type = DriverOptionType.Bool, Default = "false")]
public sealed class SimulatorDriver : PlcDriverBase
{
    /// <summary>连接。</summary>
    private bool _connected;

    /// <summary>驱动键。</summary>
    public override string DriverKey => "Simulator";

    /// <summary>是否连接。</summary>
    public override bool IsConnected => _connected;

    /// <summary>实例。</summary>
    private string Instance => GetOption("Instance", "default");

    /// <summary>连接。</summary>
    public override Task<DeviceResult> ConnectAsync(CancellationToken ct)
    {
        if (GetBool("FailConnect", false)) return Task.FromResult(DeviceResult.Fail("模拟连接失败"));
        _connected = true;
        return Task.FromResult(DeviceResult.Ok());
    }

    /// <summary>断开。</summary>
    public override Task DisconnectAsync()
    {
        _connected = false;
        return Task.CompletedTask;
    }

    /// <summary>读取核心。</summary>
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

    /// <summary>写入核心。</summary>
    protected override DeviceResult WriteCore(TagDefinition tag, object raw)
    {
        SimulatorMemory.Set(Instance, tag.Address, raw);
        return DeviceResult.Ok();
    }

    /// <summary>默认。</summary>
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
