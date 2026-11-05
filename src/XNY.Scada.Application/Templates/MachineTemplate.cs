using XNY.Scada.Application.Production;
using XNY.Scada.Application.Settings;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Model;

namespace XNY.Scada.Application.Templates;

/// <summary>机型模板 = 点位模板 + 流程定义 + 业务参数（配方/分档） + 界面布局 + MES 接口</summary>
public sealed class MachineTemplate
{
    /// <summary>机型编码。</summary>
    public string MachineType { get; set; } = "";

    /// <summary>机台模板名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>机台模板说明。</summary>
    public string? Description { get; set; }

    /// <summary>参数。</summary>
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>布局。</summary>
    public MachineLayout Layout { get; set; } = new();

    /// <summary>设备数量。</summary>
    public List<TemplateDevice> Devices { get; set; } = [];

    /// <summary>报警。</summary>
    public List<TemplateAlarm> Alarms { get; set; } = [];

    /// <summary>配方数量。</summary>
    public List<TemplateRecipe> Recipes { get; set; } = [];

    /// <summary>MES 接口数量。</summary>
    public List<MesInterfaceEntity> MesInterfaces { get; set; } = [];

    /// <summary>流程数量。</summary>
    public List<FlowDocument> Flows { get; set; } = [];

    /// <summary>加载。</summary>
    public static MachineTemplate Load(string path) =>
        Json.Deserialize<MachineTemplate>(File.ReadAllText(path)) ?? throw new InvalidDataException($"模板为空: {path}");
}

/// <summary>模板中的一台设备及其通讯参数、点位组。</summary>
public sealed class TemplateDevice
{
    /// <summary>模板设备名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>驱动键。</summary>
    public string DriverKey { get; set; } = "Simulator";
    /// <summary>选项。</summary>
    public Dictionary<string, string> Options { get; set; } = new();
    /// <summary>模板设备说明。</summary>
    public string? Description { get; set; }
    /// <summary>组。</summary>
    public List<TemplateTagGroup> Groups { get; set; } = [];
}

/// <summary>同一扫描周期下的一组点位。</summary>
public sealed class TemplateTagGroup
{
    /// <summary>模板标记组名称。</summary>
    public string Name { get; set; } = "Default";
    /// <summary>扫描周期（毫秒）。</summary>
    public int ScanIntervalMs { get; set; } = 100;
    /// <summary>点位数量。</summary>
    public List<TemplateTag> Tags { get; set; } = [];
}

/// <summary>点位模板。实际值 = 原始值 × Scale + Offset。</summary>
public sealed class TemplateTag
{
    /// <summary>模板标记名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>PLC 地址。</summary>
    public string Address { get; set; } = "";
    /// <summary>数据类型。</summary>
    public TagDataType DataType { get; set; }
    /// <summary>长度。</summary>
    public int Length { get; set; }
    /// <summary>缩放系数。</summary>
    public double Scale { get; set; } = 1;
    /// <summary>偏移量。</summary>
    public double Offset { get; set; }
    /// <summary>访问方向。</summary>
    public TagAccess Access { get; set; } = TagAccess.ReadWrite;
    /// <summary>模板标记说明。</summary>
    public string? Description { get; set; }
}

/// <summary>绑定到点位全名的报警规则，随模板一并导入。</summary>
public sealed class TemplateAlarm
{
    /// <summary>点位全名。</summary>
    public string TagName { get; set; } = "";
    /// <summary>模板报警种类。</summary>
    public AlarmKind Kind { get; set; }
    /// <summary>报警阈值。</summary>
    public double Threshold { get; set; }
    /// <summary>模板报警级别。</summary>
    public AlarmLevel Level { get; set; } = AlarmLevel.Warning;
    /// <summary>消息。</summary>
    public string Message { get; set; } = "";
}

/// <summary>模板自带配方。Activate 为 true 时导入后设为当前配方。</summary>
public sealed class TemplateRecipe
{
    /// <summary>模板配方编码。</summary>
    public string Code { get; set; } = "";
    /// <summary>模板配方名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>产品型号。</summary>
    public string ProductModel { get; set; } = "";
    /// <summary>参数。</summary>
    public Dictionary<string, object?> Params { get; set; } = new();
    /// <summary>规则。</summary>
    public List<TemplateGradingRule> Rules { get; set; } = [];
    /// <summary>是否激活。</summary>
    public bool Activate { get; set; }
}

/// <summary>一条分档或 NG 规则。按 Priority 从小到大匹配，命中即停止。</summary>
public sealed class TemplateGradingRule
{
    /// <summary>档位号。</summary>
    public int BinNo { get; set; }
    /// <summary>档位名称。</summary>
    public string BinName { get; set; } = "";
    /// <summary>优先级，数值越小越先匹配。</summary>
    public int Priority { get; set; }
    /// <summary>条件。</summary>
    public List<GradingCondition> Conditions { get; set; } = [];
    /// <summary>附加表达式。</summary>
    public string? Expression { get; set; }
    /// <summary>满箱数量。</summary>
    public int BoxCapacity { get; set; }
    /// <summary>是否判定为 NG。</summary>
    public bool IsNg { get; set; }
    /// <summary>NG 代码。</summary>
    public string? NgCode { get; set; }
}
