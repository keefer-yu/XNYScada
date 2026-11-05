using HslCommunication.Core.Device;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Inovance;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Omron;
using HslCommunication.Profinet.Siemens;

namespace XNY.Scada.Device.Hsl;

/// <summary>西门子S7驱动。</summary>
[PlcDriver("Siemens.S7", DisplayName = "西门子 S7", Vendor = "Siemens",
    AddressHelp = "M100.0 / DB1.0.0 / DB1.10 / I0.0 / Q0.0；字符串用 DB1.20 并设置长度")]
[DriverOption("Port", "端口", Type = DriverOptionType.Int, Default = "102", Order = 1)]
[DriverOption("CpuType", "CPU 型号", Type = DriverOptionType.Enum, Default = "S1200", Choices = "S1200,S1500,S300,S400,S200Smart,S200", Order = 2)]
[DriverOption("Rack", "机架号", Type = DriverOptionType.Int, Default = "0", Order = 3)]
[DriverOption("Slot", "槽号", Type = DriverOptionType.Int, Default = "0", Order = 4)]
public sealed class SiemensS7Driver : HslDriverBase
{
    /// <summary>驱动键。</summary>
    public override string DriverKey => "Siemens.S7";

    /// <summary>创建设备。</summary>
    protected override DeviceTcpNet CreateDevice()
    {
        var cpu = Enum.Parse<SiemensPLCS>(GetOption("CpuType", "S1200"), true);
        var plc = new SiemensS7Net(cpu, Ip) { Port = GetInt("Port", 102) };
        if (cpu is SiemensPLCS.S300 or SiemensPLCS.S400 or SiemensPLCS.S1200 or SiemensPLCS.S1500)
        {
            plc.Rack = (byte)GetInt("Rack", 0);
            plc.Slot = (byte)GetInt("Slot", cpu is SiemensPLCS.S300 ? 2 : 0);
        }
        return plc;
    }
}

/// <summary>三菱MC驱动。</summary>
[PlcDriver("Mitsubishi.MC", DisplayName = "三菱 MC 协议", Vendor = "Mitsubishi",
    AddressHelp = "M100 / D100 / X0 / Y0 / W0；Q/L/FX5U/iQ-R 使用 3E 帧")]
[DriverOption("Port", "端口", Type = DriverOptionType.Int, Default = "6000", Order = 1)]
[DriverOption("Frame", "帧格式", Type = DriverOptionType.Enum, Default = "3E-Binary", Choices = "3E-Binary,3E-ASCII,A1E", Order = 2)]
public sealed class MitsubishiMcDriver : HslDriverBase
{
    /// <summary>驱动键。</summary>
    public override string DriverKey => "Mitsubishi.MC";

    /// <summary>创建设备。</summary>
    protected override DeviceTcpNet CreateDevice()
    {
        var port = GetInt("Port", 6000);
        return GetOption("Frame", "3E-Binary") switch
        {
            "3E-ASCII" => new MelsecMcAsciiNet(Ip, port),
            "A1E" => new MelsecA1ENet(Ip, port),
            _ => new MelsecMcNet(Ip, port)
        };
    }
}

/// <summary>欧姆龙FINS驱动。</summary>
[PlcDriver("Omron.FinsTcp", DisplayName = "欧姆龙 FINS TCP", Vendor = "Omron",
    AddressHelp = "D100 / W100.1 / H100 / C100.0；CP/CJ/CS 系列")]
[DriverOption("Port", "端口", Type = DriverOptionType.Int, Default = "9600", Order = 1)]
[DriverOption("DA2", "目标单元号 DA2", Type = DriverOptionType.Int, Default = "0", Order = 2)]
public sealed class OmronFinsDriver : HslDriverBase
{
    /// <summary>驱动键。</summary>
    public override string DriverKey => "Omron.FinsTcp";

    /// <summary>创建设备。</summary>
    protected override DeviceTcpNet CreateDevice() =>
        new OmronFinsNet(Ip, GetInt("Port", 9600)) { DA2 = (byte)GetInt("DA2", 0) };
}

/// <summary>欧姆龙CIP驱动。</summary>
[PlcDriver("Omron.Cip", DisplayName = "欧姆龙 CIP (NJ/NX)", Vendor = "Omron",
    AddressHelp = "使用变量名（标签名），如 Run_Flag、Data[0]")]
[DriverOption("Port", "端口", Type = DriverOptionType.Int, Default = "44818", Order = 1)]
[DriverOption("Slot", "槽号", Type = DriverOptionType.Int, Default = "0", Order = 2)]
public sealed class OmronCipDriver : HslDriverBase
{
    /// <summary>驱动键。</summary>
    public override string DriverKey => "Omron.Cip";

    /// <summary>创建设备。</summary>
    protected override DeviceTcpNet CreateDevice() =>
        new OmronCipNet(Ip, GetInt("Port", 44818)) { Slot = (byte)GetInt("Slot", 0) };
}

/// <summary>汇川驱动。</summary>
[PlcDriver("Inovance.Tcp", DisplayName = "汇川 PLC (Modbus TCP)", Vendor = "Inovance",
    AddressHelp = "H3U/H5U: M100 / D100 / X0 / Y0；AM: MW100 / QX0.0")]
[DriverOption("Port", "端口", Type = DriverOptionType.Int, Default = "502", Order = 1)]
[DriverOption("Series", "系列", Type = DriverOptionType.Enum, Default = "H5U", Choices = "AM,H3U,H5U", Order = 2)]
[DriverOption("Station", "站号", Type = DriverOptionType.Int, Default = "1", Order = 3)]
public sealed class InovanceDriver : HslDriverBase
{
    /// <summary>驱动键。</summary>
    public override string DriverKey => "Inovance.Tcp";

    /// <summary>创建设备。</summary>
    protected override DeviceTcpNet CreateDevice() =>
        new InovanceTcpNet(Enum.Parse<InovanceSeries>(GetOption("Series", "H5U"), true), Ip, GetInt("Port", 502), (byte)GetInt("Station", 1));
}

/// <summary>ModbusTCP驱动。</summary>
[PlcDriver("Modbus.Tcp", DisplayName = "通用 Modbus TCP", Vendor = "Generic",
    AddressHelp = "保持寄存器 100 / 线圈 x=1;100 / 输入寄存器 x=4;100")]
[DriverOption("Port", "端口", Type = DriverOptionType.Int, Default = "502", Order = 1)]
[DriverOption("Station", "站号", Type = DriverOptionType.Int, Default = "1", Order = 2)]
[DriverOption("AddressStartWithZero", "地址从 0 开始", Type = DriverOptionType.Bool, Default = "true", Order = 3)]
public sealed class ModbusTcpDriver : HslDriverBase
{
    /// <summary>驱动键。</summary>
    public override string DriverKey => "Modbus.Tcp";

    /// <summary>创建设备。</summary>
    protected override DeviceTcpNet CreateDevice() =>
        new ModbusTcpNet(Ip, GetInt("Port", 502), (byte)GetInt("Station", 1))
        {
            AddressStartWithZero = GetBool("AddressStartWithZero", true)
        };
}
