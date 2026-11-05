using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Mes;

public sealed class MesOptions
{
    public const string Section = "Mes";

    /// <summary>全局开关，关闭后所有接口视为禁用（离线生产）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>MES 回调监听前缀，如 http://+:8090/scada/，为空不监听</summary>
    public string? CallbackPrefix { get; set; }

    public int OutboxIntervalSeconds { get; set; } = 30;

    public int OutboxMaxRetry { get; set; } = 100;
}

public sealed record MesResult
{
    public bool Success { get; init; }

    /// <summary>网络不可达或超时</summary>
    public bool Offline { get; init; }

    /// <summary>已缓存到发件箱等待补传</summary>
    public bool Queued { get; init; }

    /// <summary>接口被禁用，未实际调用</summary>
    public bool Bypassed { get; init; }

    public string Message { get; init; } = "";

    public IReadOnlyDictionary<string, object?> Fields { get; init; } = new Dictionary<string, object?>();

    public string? RawResponse { get; init; }

    public long ElapsedMs { get; init; }
}

public sealed class MesCallbackEventArgs(string path, IReadOnlyDictionary<string, object?> data) : EventArgs
{
    public string Path { get; } = path;
    public IReadOnlyDictionary<string, object?> Data { get; } = data;

    /// <summary>回写给 MES 的响应体</summary>
    public string ResponseBody { get; set; } = "{\"code\":0,\"msg\":\"ok\"}";
}

public interface IMesClient
{
    event EventHandler<MesCallLogEntity>? CallCompleted;

    Task<MesResult> CallAsync(string interfaceCode, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);

    /// <summary>使用未保存的配置调试调用，不写发件箱</summary>
    Task<MesResult> TestAsync(MesInterfaceEntity config, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);

    Task ReloadAsync();
}
