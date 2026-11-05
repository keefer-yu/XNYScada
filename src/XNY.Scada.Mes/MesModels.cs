using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Mes;

/// <summary>MES的配置项。</summary>
public sealed class MesOptions
{
    /// <summary>配置节名称。</summary>
    public const string Section = "Mes";

    /// <summary>全局开关，关闭后所有接口视为禁用（离线生产）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>MES 回调监听前缀，如 http://+:8090/scada/，为空不监听</summary>
    public string? CallbackPrefix { get; set; }

    /// <summary>发件箱补传间隔（秒）。</summary>
    public int OutboxIntervalSeconds { get; set; } = 30;

    /// <summary>发件箱单条最大重试次数。</summary>
    public int OutboxMaxRetry { get; set; } = 100;
}

/// <summary>MES结果。</summary>
public sealed record MesResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>网络不可达或超时</summary>
    public bool Offline { get; init; }

    /// <summary>已缓存到发件箱等待补传</summary>
    public bool Queued { get; init; }

    /// <summary>接口被禁用，未实际调用</summary>
    public bool Bypassed { get; init; }

    /// <summary>消息。</summary>
    public string Message { get; init; } = "";

    /// <summary>映射后的响应字段。</summary>
    public IReadOnlyDictionary<string, object?> Fields { get; init; } = new Dictionary<string, object?>();

    /// <summary>原始响应正文。</summary>
    public string? RawResponse { get; init; }

    /// <summary>耗时（毫秒）。</summary>
    public long ElapsedMs { get; init; }
}

/// <summary>MES回调事件参数。</summary>
/// <param name="path">路径。</param>
/// <param name="data">数据。</param>
public sealed class MesCallbackEventArgs(string path, IReadOnlyDictionary<string, object?> data) : EventArgs
{
    /// <summary>MES回调事件参数路径。</summary>
    public string Path { get; } = path;
    /// <summary>数据。</summary>
    public IReadOnlyDictionary<string, object?> Data { get; } = data;

    /// <summary>回写给 MES 的响应体</summary>
    public string ResponseBody { get; set; } = "{\"code\":0,\"msg\":\"ok\"}";
}

/// <summary>MES客户区的契约。</summary>
public interface IMesClient
{
    /// <summary>MES 调用结束发生变化时触发。</summary>
    event EventHandler<MesCallLogEntity>? CallCompleted;

    /// <summary>调用。</summary>
    Task<MesResult> CallAsync(string interfaceCode, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);

    /// <summary>使用未保存的配置调试调用，不写发件箱</summary>
    Task<MesResult> TestAsync(MesInterfaceEntity config, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);

    /// <summary>重新加载。</summary>
    Task ReloadAsync();
}
