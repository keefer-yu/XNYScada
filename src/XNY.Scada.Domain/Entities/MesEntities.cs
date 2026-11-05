using SqlSugar;

namespace XNY.Scada.Domain.Entities;

[SugarTable("mes_interface")]
public class MesInterfaceEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 512)]
    public string Url { get; set; } = "";

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

    public int TimeoutMs { get; set; } = 5000;

    public int RetryCount { get; set; } = 1;

    /// <summary>断网时缓存到发件箱，联网后补传（仅适合上传类接口）</summary>
    public bool CacheWhenOffline { get; set; }

    /// <summary>禁用时调用直接返回成功（离线生产模式）</summary>
    public bool Enabled { get; set; } = true;
}

[SugarTable("mes_call_log")]
public class MesCallLogEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string InterfaceCode { get; set; } = "";

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? Request { get; set; }

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? Response { get; set; }

    public bool Success { get; set; }

    public long ElapsedMs { get; set; }

    [SugarColumn(Length = 1024)]
    public string? Error { get; set; }
}

[SugarTable("mes_outbox")]
public class MesOutboxEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string InterfaceCode { get; set; } = "";

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Payload { get; set; } = "";

    public MesOutboxStatus Status { get; set; }

    public int RetryCount { get; set; }

    [SugarColumn(Length = 1024)]
    public string? LastError { get; set; }

    public DateTime? SentAt { get; set; }
}
