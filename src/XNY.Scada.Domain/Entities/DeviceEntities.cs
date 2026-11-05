using SqlSugar;

namespace XNY.Scada.Domain.Entities;

[SugarTable("dev_device")]
public class DeviceEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    /// <summary>驱动键，如 Siemens.S7、Mitsubishi.MC、Omron.FinsTcp、Inovance.Tcp</summary>
    [SugarColumn(Length = 64)]
    public string DriverKey { get; set; } = "";

    /// <summary>驱动参数 JSON（键值对）</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string OptionsJson { get; set; } = "{}";

    public bool Enabled { get; set; } = true;

    public string? Description { get; set; }
}

[SugarTable("dev_tag_group")]
public class TagGroupEntity : EntityBase
{
    public long DeviceId { get; set; }

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    public int ScanIntervalMs { get; set; } = 100;
}

[SugarTable("dev_tag")]
public class TagEntity : EntityBase
{
    public long DeviceId { get; set; }

    public long GroupId { get; set; }

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Address { get; set; } = "";

    public TagDataType DataType { get; set; }

    /// <summary>字符串长度（字节）或数组长度</summary>
    public int Length { get; set; }

    public double Scale { get; set; } = 1;

    public double Offset { get; set; }

    public TagAccess Access { get; set; } = TagAccess.ReadWrite;

    public string? Description { get; set; }
}

[SugarTable("dev_alarm_def")]
public class AlarmDefinitionEntity : EntityBase
{
    /// <summary>点位全名：设备名.点位名</summary>
    [SugarColumn(Length = 128)]
    public string TagName { get; set; } = "";

    public AlarmKind Kind { get; set; }

    public double Threshold { get; set; }

    public AlarmLevel Level { get; set; } = AlarmLevel.Warning;

    [SugarColumn(Length = 256)]
    public string Message { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

[SugarTable("dev_alarm_record")]
public class AlarmRecordEntity : EntityBase
{
    [SugarColumn(Length = 128)]
    public string Source { get; set; } = "";

    [SugarColumn(Length = 512)]
    public string Message { get; set; } = "";

    public AlarmLevel Level { get; set; }

    public DateTime RaisedAt { get; set; } = DateTime.Now;

    public DateTime? ClearedAt { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    public string? AcknowledgedBy { get; set; }

    public bool IsActive { get; set; } = true;
}
