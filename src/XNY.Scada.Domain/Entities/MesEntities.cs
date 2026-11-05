using SqlSugar;

namespace XNY.Scada.Domain.Entities;

/// <summary>MES接口实体，对应表 mes_interface。</summary>
[SugarTable("mes_interface")]
public class MesInterfaceEntity : EntityBase
{
    /// <summary>MES接口编码。</summary>
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    /// <summary>MES接口名称。</summary>
    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    /// <summary>请求地址。</summary>
    [SugarColumn(Length = 512)]
    public string Url { get; set; } = "";

    /// <summary>HTTP 方法。</summary>
    [SugarColumn(Length = 16)]
    public string Method { get; set; } = "POST";

    /// <summary>请求头 JSON 对象</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string HeadersJson { get; set; } = "{}";

    /// <summary>请求体模板，使用 ${name} 占位</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string RequestTemplate { get; set; } = "{}";

    /// <summary>响应映射 JSON：{ "变量名": "data.result" }</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string ResponseMapJson { get; set; } = "{}";

    /// <summary>成功判定表达式（基于映射后的字段），为空表示 HTTP 2xx 即成功</summary>
    [SugarColumn(Length = 512)]
    public string? SuccessExpression { get; set; }

    /// <summary>超时（毫秒）。</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>重试次数。</summary>
    public int RetryCount { get; set; } = 1;

    /// <summary>断网时缓存到发件箱，联网后补传（仅适合上传类接口）</summary>
    public bool CacheWhenOffline { get; set; }

    /// <summary>禁用时调用直接返回成功（离线生产模式）</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>MES调用日志实体，对应表 mes_call_log。</summary>
[SugarTable("mes_call_log")]
public class MesCallLogEntity : EntityBase
{
    /// <summary>接口代码。</summary>
    [SugarColumn(Length = 64)]
    public string InterfaceCode { get; set; } = "";

    /// <summary>请求。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? Request { get; set; }

    /// <summary>响应。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? Response { get; set; }

    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public long ElapsedMs { get; set; }

    /// <summary>错误。</summary>
    [SugarColumn(Length = 1024)]
    public string? Error { get; set; }
}

/// <summary>MES发件箱实体，对应表 mes_outbox。</summary>
[SugarTable("mes_outbox")]
public class MesOutboxEntity : EntityBase
{
    /// <summary>接口代码。</summary>
    [SugarColumn(Length = 64)]
    public string InterfaceCode { get; set; } = "";

    /// <summary>载荷。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Payload { get; set; } = "";

    /// <summary>MES发件箱状态。</summary>
    public MesOutboxStatus Status { get; set; }

    /// <summary>重试次数。</summary>
    public int RetryCount { get; set; }

    /// <summary>最近一次错误。</summary>
    [SugarColumn(Length = 1024)]
    public string? LastError { get; set; }

    /// <summary>已发送于。</summary>
    public DateTime? SentAt { get; set; }
}
