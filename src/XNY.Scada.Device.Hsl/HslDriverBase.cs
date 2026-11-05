using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using XNY.Scada.Domain;

namespace XNY.Scada.Device.Hsl;

/// <summary>HSL驱动基类。</summary>
[DriverOption("Ip", "IP 地址", Default = "192.168.0.10", Order = 0)]
[DriverOption("ConnectTimeoutMs", "连接超时(ms)", Type = DriverOptionType.Int, Default = "3000", Order = 90)]
[DriverOption("ReceiveTimeoutMs", "接收超时(ms)", Type = DriverOptionType.Int, Default = "3000", Order = 91)]
public abstract class HslDriverBase : PlcDriverBase
{
    /// <summary>设备。</summary>
    private DeviceTcpNet? _device;
    /// <summary>连接。</summary>
    private volatile bool _connected;

    /// <summary>是否连接。</summary>
    public override bool IsConnected => _connected;

    /// <summary>设备。</summary>
    protected DeviceTcpNet Device => _device ?? throw new InvalidOperationException("驱动未连接");

    /// <summary>IP 地址。</summary>
    protected string Ip => GetOption("Ip", "127.0.0.1");

    /// <summary>创建设备。</summary>
    protected abstract DeviceTcpNet CreateDevice();

    /// <summary>连接。</summary>
    public override Task<DeviceResult> ConnectAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            _device?.ConnectClose();
            _device = CreateDevice();
            _device.ConnectTimeOut = GetInt("ConnectTimeoutMs", 3000);
            _device.ReceiveTimeOut = GetInt("ReceiveTimeoutMs", 3000);
            var r = _device.ConnectServer();
            _connected = r.IsSuccess;
            return r.IsSuccess ? DeviceResult.Ok() : DeviceResult.Fail(r.Message);
        }, ct);
    }

    /// <summary>断开。</summary>
    public override Task DisconnectAsync()
    {
        _connected = false;
        _device?.ConnectClose();
        _device = null;
        return Task.CompletedTask;
    }

    /// <summary>读取核心。</summary>
    protected override DeviceResult<object?> ReadCore(TagDefinition tag)
    {
        IReadWriteNet d = Device;
        var a = tag.Address;
        return tag.DataType switch
        {
            TagDataType.Bool => Map(d.ReadBool(a)),
            TagDataType.Int16 => Map(d.ReadInt16(a)),
            TagDataType.UInt16 => Map(d.ReadUInt16(a)),
            TagDataType.Int32 => Map(d.ReadInt32(a)),
            TagDataType.UInt32 => Map(d.ReadUInt32(a)),
            TagDataType.Int64 => Map(d.ReadInt64(a)),
            TagDataType.Float => Map(d.ReadFloat(a)),
            TagDataType.Double => Map(d.ReadDouble(a)),
            TagDataType.String => MapString(d.ReadString(a, (ushort)Math.Max(1, tag.Length))),
            _ => DeviceResult<object?>.Fail($"不支持的数据类型 {tag.DataType}")
        };
    }

    /// <summary>写入核心。</summary>
    protected override DeviceResult WriteCore(TagDefinition tag, object raw)
    {
        IReadWriteNet d = Device;
        var a = tag.Address;
        var r = raw switch
        {
            bool v => d.Write(a, v),
            short v => d.Write(a, v),
            ushort v => d.Write(a, v),
            int v => d.Write(a, v),
            uint v => d.Write(a, v),
            long v => d.Write(a, v),
            float v => d.Write(a, v),
            double v => d.Write(a, v),
            string v => WriteString(d, tag, v),
            _ => new OperateResult($"不支持的写入类型 {raw.GetType().Name}")
        };
        if (!r.IsSuccess) MarkFault(r);
        return r.IsSuccess ? DeviceResult.Ok() : DeviceResult.Fail(r.Message);
    }

    /// <summary>写入字符串。</summary>
    protected virtual OperateResult WriteString(IReadWriteNet d, TagDefinition tag, string value) =>
        tag.Length > 0 ? d.Write(tag.Address, value, tag.Length) : d.Write(tag.Address, value);

    /// <summary>映射。</summary>
    private DeviceResult<object?> Map<T>(OperateResult<T> r)
    {
        if (r.IsSuccess) return DeviceResult<object?>.Ok(r.Content);
        MarkFault(r);
        return DeviceResult<object?>.Fail(r.Message);
    }

    /// <summary>映射字符串。</summary>
    private DeviceResult<object?> MapString(OperateResult<string> r)
    {
        if (r.IsSuccess) return DeviceResult<object?>.Ok(r.Content?.TrimEnd('\0', ' '));
        MarkFault(r);
        return DeviceResult<object?>.Fail(r.Message);
    }

    /// <summary>HSL 负数错误码表示通讯层异常（断线、超时），此时标记为未连接触发重连</summary>
    private void MarkFault(OperateResult r)
    {
        if (r.ErrorCode < 0) _connected = false;
    }
}
