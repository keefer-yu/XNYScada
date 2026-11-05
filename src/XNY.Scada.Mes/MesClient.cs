using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Expressions;

namespace XNY.Scada.Mes;

/// <summary>
/// 配置驱动的 MES HTTP 客户端：请求模板渲染 → 发送（重试/超时）→ 响应字段映射 → 成功表达式判定；
/// 网络异常时可写入发件箱等待补传。
/// </summary>
public sealed class MesClient(
    IHttpClientFactory httpFactory,
    ISqlSugarClient db,
    MesOptions options,
    ILogger<MesClient> logger) : IMesClient
{
    public const string HttpClientName = "mes";

    private ConcurrentDictionary<string, MesInterfaceEntity> _configs = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _loaded;

    public event EventHandler<MesCallLogEntity>? CallCompleted;

    public async Task ReloadAsync()
    {
        var list = await db.Queryable<MesInterfaceEntity>().ToListAsync();
        _configs = new ConcurrentDictionary<string, MesInterfaceEntity>(list.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        _loaded = true;
    }

    public async Task<MesResult> CallAsync(string interfaceCode, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!_loaded) await ReloadAsync();
        if (!_configs.TryGetValue(interfaceCode, out var config))
            return new MesResult { Success = false, Message = $"MES 接口未配置: {interfaceCode}" };
        if (!options.Enabled || !config.Enabled)
            return new MesResult { Success = true, Bypassed = true, Message = "接口已禁用，跳过调用" };

        var body = RenderBody(config, args);
        var result = await SendAsync(config, body, ct);
        if (result.Offline && config.CacheWhenOffline)
        {
            await db.Insertable(new MesOutboxEntity { InterfaceCode = config.Code, Payload = body, Status = MesOutboxStatus.Pending, LastError = result.Message })
                .ExecuteCommandAsync();
            logger.LogWarning("MES {Code} 离线，已缓存待补传", config.Code);
            return result with { Success = true, Queued = true, Message = "MES 离线，数据已缓存待补传" };
        }
        return result;
    }

    public Task<MesResult> TestAsync(MesInterfaceEntity config, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default) =>
        SendAsync(config, RenderBody(config, args), ct);

    /// <summary>发件箱补传使用：直接发送已渲染的请求体</summary>
    internal async Task<MesResult> ResendAsync(string interfaceCode, string body, CancellationToken ct)
    {
        if (!_loaded) await ReloadAsync();
        return _configs.TryGetValue(interfaceCode, out var config)
            ? await SendAsync(config, body, ct)
            : new MesResult { Success = false, Message = $"MES 接口未配置: {interfaceCode}" };
    }

    public const string SoapMethod = "SOAP";

    /// <summary>Method 配置为 SOAP 时按 WebService 调用：POST text/xml，请求模板为 SOAP 信封，响应 XML 转为 JSON 后映射</summary>
    public static bool IsSoap(MesInterfaceEntity config) => string.Equals(config.Method, SoapMethod, StringComparison.OrdinalIgnoreCase);

    public static string RenderBody(string template, IReadOnlyDictionary<string, object?> args) =>
        ScadaExpression.Render(template, args, formatter: JsonFormat);

    public static string RenderBody(MesInterfaceEntity config, IReadOnlyDictionary<string, object?> args) =>
        IsSoap(config) ? ScadaExpression.Render(config.RequestTemplate, args, formatter: XmlFormat) : RenderBody(config.RequestTemplate, args);

    private static string XmlFormat(object? v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        string s => System.Security.SecurityElement.Escape(s),
        _ => System.Security.SecurityElement.Escape(JsonSerializer.Serialize(v, Json.Options))
    };

    /// <summary>占位符一般写在 JSON 字符串引号内，因此字符串做 JSON 转义但不加引号</summary>
    private static string JsonFormat(object? v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        string s => JsonEncodedText.Encode(s, Json.Options.Encoder).ToString(),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(v, Json.Options)
    };

    private async Task<MesResult> SendAsync(MesInterfaceEntity config, string body, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var log = new MesCallLogEntity { InterfaceCode = config.Code, Request = body };
        MesResult result;
        try
        {
            var pipeline = BuildPipeline(config);
            var (status, text) = await pipeline.ExecuteAsync(async token => await SendOnceAsync(config, body, token), ct);
            log.Response = text;
            result = Interpret(config, status, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or Polly.Timeout.TimeoutRejectedException && !ct.IsCancellationRequested)
        {
            result = new MesResult { Success = false, Offline = true, Message = $"MES 通讯失败: {ex.Message}" };
        }
        catch (Exception ex)
        {
            result = new MesResult { Success = false, Message = $"MES 调用异常: {ex.Message}" };
        }

        result = result with { ElapsedMs = sw.ElapsedMilliseconds };
        log.Success = result.Success;
        log.ElapsedMs = result.ElapsedMs;
        log.Error = result.Success ? null : result.Message;
        try
        {
            await db.Insertable(log).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "写 MES 调用日志失败");
        }
        CallCompleted?.Invoke(this, log);
        return result;
    }

    private static ResiliencePipeline<(int, string)> BuildPipeline(MesInterfaceEntity config)
    {
        var builder = new ResiliencePipelineBuilder<(int, string)>();
        if (config.RetryCount > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<(int, string)>
            {
                MaxRetryAttempts = config.RetryCount,
                Delay = TimeSpan.FromMilliseconds(300),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<(int, string)>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => r.Item1 >= 500)
            });
        }
        return builder.Build();
    }

    private async Task<(int, string)> SendOnceAsync(MesInterfaceEntity config, string body, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Math.Max(500, config.TimeoutMs));
        var http = httpFactory.CreateClient(HttpClientName);
        var soap = IsSoap(config);
        var method = soap ? HttpMethod.Post : new HttpMethod(config.Method.ToUpperInvariant());
        var url = config.Url;
        var headers = Json.Deserialize<Dictionary<string, string>>(config.HeadersJson) ?? new();
        var contentType = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value
                          ?? (soap ? "text/xml; charset=utf-8" : "application/json");
        using var request = new HttpRequestMessage(method, url);
        if (method == HttpMethod.Get)
        {
            var query = string.Join("&", Json.ToDictionary(body).Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(ScadaExpression.ToInvariantString(kv.Value))}"));
            if (query.Length > 0) request.RequestUri = new Uri(url + (url.Contains('?') ? "&" : "?") + query);
        }
        else
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        }
        foreach (var kv in headers.Where(h => !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
        using var response = await http.SendAsync(request, cts.Token);
        var text = await response.Content.ReadAsStringAsync(cts.Token);
        return ((int)response.StatusCode, text);
    }

    public static MesResult Interpret(MesInterfaceEntity config, int status, string text)
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["HttpStatus"] = (long)status };
        try
        {
            var json = string.IsNullOrWhiteSpace(text) ? "{}" : text.TrimStart().StartsWith('<') ? XmlToJson(text) : text;
            using var doc = JsonDocument.Parse(json);
            var map = Json.Deserialize<Dictionary<string, string>>(config.ResponseMapJson) ?? new();
            foreach (var (name, path) in map) fields[name] = JsonPath.Select(doc.RootElement, path);
        }
        catch (Exception ex) when (ex is JsonException or System.Xml.XmlException)
        {
            fields["Raw"] = text;
        }

        var httpOk = status is >= 200 and < 300;
        bool ok;
        string message;
        if (!httpOk)
        {
            ok = false;
            message = $"HTTP {status}";
        }
        else if (string.IsNullOrWhiteSpace(config.SuccessExpression))
        {
            ok = true;
            message = "OK";
        }
        else
        {
            try
            {
                ok = ScadaExpression.EvaluateBool(config.SuccessExpression, fields);
                message = ok ? "OK" : Convert.ToString(fields.GetValueOrDefault("Message") ?? fields.GetValueOrDefault("Msg") ?? "MES 返回失败", CultureInfo.InvariantCulture)!;
            }
            catch (Exception ex)
            {
                ok = false;
                message = $"成功判定表达式错误: {ex.Message}";
            }
        }
        return new MesResult { Success = ok, Message = message, Fields = fields, RawResponse = text };
    }

    /// <summary>XML 转 JSON（去命名空间；同名子元素转数组；叶子元素为字符串），路径如 Envelope.Body.UploadResponse.UploadResult</summary>
    public static string XmlToJson(string xml)
    {
        var root = System.Xml.Linq.XElement.Parse(xml);
        return JsonSerializer.Serialize(new Dictionary<string, object?> { [root.Name.LocalName] = Convert(root) });

        static object? Convert(System.Xml.Linq.XElement e)
        {
            if (!e.HasElements) return e.Value;
            var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in e.Elements().GroupBy(c => c.Name.LocalName))
            {
                obj[g.Key] = g.Count() == 1 ? Convert(g.First()) : g.Select(Convert).ToList();
            }
            return obj;
        }
    }
}
