using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XNY.Scada.Domain;

namespace XNY.Scada.Mes;

/// <summary>接收 MES 主动推送（如工单下发、托盘信息），转发为流程触发事件</summary>
/// <param name="options">配置项。</param>
/// <param name="logger">日志记录器。</param>
public sealed class MesCallbackListener(MesOptions options, ILogger<MesCallbackListener> logger) : BackgroundService
{
    /// <summary>回调已接收发生变化时触发。</summary>
    public event EventHandler<MesCallbackEventArgs>? CallbackReceived;

    /// <summary>执行。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.CallbackPrefix)) return;
        using var listener = new HttpListener();
        listener.Prefixes.Add(options.CallbackPrefix.EndsWith('/') ? options.CallbackPrefix : options.CallbackPrefix + "/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            logger.LogError(ex, "MES 回调监听启动失败（可能需要 netsh http add urlacl 授权）：{Prefix}", options.CallbackPrefix);
            return;
        }
        logger.LogInformation("MES 回调监听已启动 {Prefix}", options.CallbackPrefix);
        using var reg = stoppingToken.Register(listener.Stop);
        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(ctx), stoppingToken);
        }
    }

    /// <summary>处理。</summary>
    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var data = Json.ToDictionary(body);
            foreach (var key in ctx.Request.QueryString.AllKeys.Where(k => k != null)) data[key!] = ctx.Request.QueryString[key];
            var path = ctx.Request.Url?.AbsolutePath.Trim('/') ?? "";
            var args = new MesCallbackEventArgs(path.Split('/').Last(), data);
            CallbackReceived?.Invoke(this, args);
            var bytes = Encoding.UTF8.GetBytes(args.ResponseBody);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 MES 回调失败");
            ctx.Response.StatusCode = 500;
        }
        finally
        {
            ctx.Response.Close();
        }
    }
}
