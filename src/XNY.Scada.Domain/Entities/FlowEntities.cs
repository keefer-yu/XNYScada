using SqlSugar;

namespace XNY.Scada.Domain.Entities;

[SugarTable("flow_definition")]
public class FlowDefinitionEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    [SugarColumn(Length = 64)]
    public string? MachineType { get; set; }

    /// <summary>当前生效的已发布版本号，0 表示未发布</summary>
    public int PublishedVersion { get; set; }

    /// <summary>最新版本号（含草稿）</summary>
    public int LatestVersion { get; set; }

    public bool Enabled { get; set; } = true;
}

[SugarTable("flow_version")]
public class FlowVersionEntity : EntityBase
{
    public long FlowId { get; set; }

    [SugarColumn(Length = 64)]
    public string FlowCode { get; set; } = "";

    public int Version { get; set; }

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Json { get; set; } = "";

    public FlowVersionStatus Status { get; set; }

    public string? PublishedBy { get; set; }

    public DateTime? PublishedAt { get; set; }

    public string? Remark { get; set; }
}

[SugarTable("flow_instance_log")]
public class FlowInstanceLogEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string FlowCode { get; set; } = "";

    public int Version { get; set; }

    [SugarColumn(Length = 64)]
    public string InstanceId { get; set; } = "";

    public FlowInstanceStatus Status { get; set; }

    [SugarColumn(Length = 64)]
    public string? TriggerSource { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public long DurationMs { get; set; }

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? Error { get; set; }

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? TraceJson { get; set; }
}
