using XNY.Scada.Application.Production;
using XNY.Scada.Application.Settings;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Model;

namespace XNY.Scada.Application.Templates;

/// <summary>机型模板 = 点位模板 + 流程定义 + 业务参数（配方/分档） + 界面布局 + MES 接口</summary>
public sealed class MachineTemplate
{
    public string MachineType { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public Dictionary<string, string> Params { get; set; } = new();

    public MachineLayout Layout { get; set; } = new();

    public List<TemplateDevice> Devices { get; set; } = [];

    public List<TemplateAlarm> Alarms { get; set; } = [];

    public List<TemplateRecipe> Recipes { get; set; } = [];

    public List<MesInterfaceEntity> MesInterfaces { get; set; } = [];

    public List<FlowDocument> Flows { get; set; } = [];

    public static MachineTemplate Load(string path) =>
        Json.Deserialize<MachineTemplate>(File.ReadAllText(path)) ?? throw new InvalidDataException($"模板为空: {path}");
}

public sealed class TemplateDevice
{
    public string Name { get; set; } = "";
    public string DriverKey { get; set; } = "Simulator";
    public Dictionary<string, string> Options { get; set; } = new();
    public string? Description { get; set; }
    public List<TemplateTagGroup> Groups { get; set; } = [];
}

public sealed class TemplateTagGroup
{
    public string Name { get; set; } = "Default";
    public int ScanIntervalMs { get; set; } = 100;
    public List<TemplateTag> Tags { get; set; } = [];
}

public sealed class TemplateTag
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public TagDataType DataType { get; set; }
    public int Length { get; set; }
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }
    public TagAccess Access { get; set; } = TagAccess.ReadWrite;
    public string? Description { get; set; }
}

public sealed class TemplateAlarm
{
    public string TagName { get; set; } = "";
    public AlarmKind Kind { get; set; }
    public double Threshold { get; set; }
    public AlarmLevel Level { get; set; } = AlarmLevel.Warning;
    public string Message { get; set; } = "";
}

public sealed class TemplateRecipe
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public Dictionary<string, object?> Params { get; set; } = new();
    public List<TemplateGradingRule> Rules { get; set; } = [];
    public bool Activate { get; set; }
}

public sealed class TemplateGradingRule
{
    public int BinNo { get; set; }
    public string BinName { get; set; } = "";
    public int Priority { get; set; }
    public List<GradingCondition> Conditions { get; set; } = [];
    public string? Expression { get; set; }
    public int BoxCapacity { get; set; }
    public bool IsNg { get; set; }
    public string? NgCode { get; set; }
}
