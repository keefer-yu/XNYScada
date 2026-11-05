using SqlSugar;

namespace XNY.Scada.Domain.Entities;

/// <summary>设备实体，对应表 dev_device。</summary>
[SugarTable("dev_device")]
public class DeviceEntity : EntityBase
{
    /// <summary>设备名称。</summary>
    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    /// <summary>驱动键，如 Siemens.S7、Mitsubishi.MC、Omron.FinsTcp、Inovance.Tcp</summary>
    [SugarColumn(Length = 64)]
    public string DriverKey { get; set; } = "";

    /// <summary>驱动参数 JSON（键值对）</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string OptionsJson { get; set; } = "{}";

    /// <summary>是否设备是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>设备说明。</summary>
    public string? Description { get; set; }
}

/// <summary>标记组实体，对应表 dev_tag_group。</summary>
[SugarTable("dev_tag_group")]
public class TagGroupEntity : EntityBase
{
    /// <summary>设备标识。</summary>
    public long DeviceId { get; set; }

    /// <summary>标记组名称。</summary>
    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    /// <summary>扫描周期（毫秒）。</summary>
    public int ScanIntervalMs { get; set; } = 100;
}

/// <summary>标记实体，对应表 dev_tag。</summary>
[SugarTable("dev_tag")]
public class TagEntity : EntityBase
{
    /// <summary>设备标识。</summary>
    public long DeviceId { get; set; }

    /// <summary>点位组标识。</summary>
    public long GroupId { get; set; }

    /// <summary>标记名称。</summary>
    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    /// <summary>PLC 地址。</summary>
    [SugarColumn(Length = 64)]
    public string Address { get; set; } = "";

    /// <summary>数据类型。</summary>
    public TagDataType DataType { get; set; }

    /// <summary>字符串长度（字节）或数组长度</summary>
    public int Length { get; set; }

    /// <summary>缩放系数。</summary>
    public double Scale { get; set; } = 1;

    /// <summary>偏移量。</summary>
    public double Offset { get; set; }

    /// <summary>访问方向。</summary>
    public TagAccess Access { get; set; } = TagAccess.ReadWrite;

    /// <summary>标记说明。</summary>
    public string? Description { get; set; }
}

/// <summary>报警定义实体，对应表 dev_alarm_def。</summary>
[SugarTable("dev_alarm_def")]
public class AlarmDefinitionEntity : EntityBase
{
    /// <summary>点位全名：设备名.点位名</summary>
    [SugarColumn(Length = 128)]
    public string TagName { get; set; } = "";

    /// <summary>报警定义种类。</summary>
    public AlarmKind Kind { get; set; }

    /// <summary>报警阈值。</summary>
    public double Threshold { get; set; }

    /// <summary>报警定义级别。</summary>
    public AlarmLevel Level { get; set; } = AlarmLevel.Warning;

    /// <summary>消息。</summary>
    [SugarColumn(Length = 256)]
    public string Message { get; set; } = "";

    /// <summary>是否报警定义是否启用。</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>报警记录实体，对应表 dev_alarm_record。</summary>
[SugarTable("dev_alarm_record")]
public class AlarmRecordEntity : EntityBase
{
    /// <summary>来源。</summary>
    [SugarColumn(Length = 128)]
    public string Source { get; set; } = "";

    /// <summary>消息。</summary>
    [SugarColumn(Length = 512)]
    public string Message { get; set; } = "";

    /// <summary>报警记录级别。</summary>
    public AlarmLevel Level { get; set; }

    /// <summary>报警发生时间。</summary>
    public DateTime RaisedAt { get; set; } = DateTime.Now;

    /// <summary>报警解除时间。</summary>
    public DateTime? ClearedAt { get; set; }

    /// <summary>报警确认时间。</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>报警确认人。</summary>
    public string? AcknowledgedBy { get; set; }

    /// <summary>是否仍处于活动状态。</summary>
    public bool IsActive { get; set; } = true;
}
