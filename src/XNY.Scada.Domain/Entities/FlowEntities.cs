using SqlSugar;

namespace XNY.Scada.Domain.Entities;

/// <summary>流定义实体，对应表 flow_definition。</summary>
[SugarTable("flow_definition")]
public class FlowDefinitionEntity : EntityBase
{
    /// <summary>流定义编码。</summary>
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    /// <summary>流定义名称。</summary>
    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    /// <summary>流定义说明。</summary>
    public string? Description { get; set; }

    /// <summary>机型编码。</summary>
    [SugarColumn(Length = 64)]
    public string? MachineType { get; set; }

    /// <summary>当前生效的已发布版本号，0 表示未发布</summary>
    public int PublishedVersion { get; set; }

    /// <summary>最新版本号（含草稿）</summary>
    public int LatestVersion { get; set; }

    /// <summary>是否流定义是否启用。</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>流版本实体，对应表 flow_version。</summary>
[SugarTable("flow_version")]
public class FlowVersionEntity : EntityBase
{
    /// <summary>流标识。</summary>
    public long FlowId { get; set; }

    /// <summary>流代码。</summary>
    [SugarColumn(Length = 64)]
    public string FlowCode { get; set; } = "";

    /// <summary>版本号。</summary>
    public int Version { get; set; }

    /// <summary>JSON。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Json { get; set; } = "";

    /// <summary>流版本状态。</summary>
    public FlowVersionStatus Status { get; set; }

    /// <summary>已发布按。</summary>
    public string? PublishedBy { get; set; }

    /// <summary>已发布于。</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>备注。</summary>
    public string? Remark { get; set; }
}

/// <summary>流实例日志实体，对应表 flow_instance_log。</summary>
[SugarTable("flow_instance_log")]
public class FlowInstanceLogEntity : EntityBase
{
    /// <summary>流代码。</summary>
    [SugarColumn(Length = 64)]
    public string FlowCode { get; set; } = "";

    /// <summary>版本号。</summary>
    public int Version { get; set; }

    /// <summary>实例标识。</summary>
    [SugarColumn(Length = 64)]
    public string InstanceId { get; set; } = "";

    /// <summary>流实例日志状态。</summary>
    public FlowInstanceStatus Status { get; set; }

    /// <summary>触发器源。</summary>
    [SugarColumn(Length = 64)]
    public string? TriggerSource { get; set; }

    /// <summary>已启动于。</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>已结束于。</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>时长毫秒。</summary>
    public long DurationMs { get; set; }

    /// <summary>错误。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? Error { get; set; }

    /// <summary>跟踪JSON。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? TraceJson { get; set; }
}
