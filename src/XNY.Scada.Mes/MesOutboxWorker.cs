using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Mes;

/// <summary>断网补传：周期性重发发件箱中待发送的数据，保持先进先出</summary>
/// <param name="client">客户区。</param>
/// <param name="db">数据库客户端。</param>
/// <param name="options">配置项。</param>
/// <param name="logger">日志记录器。</param>
public sealed class MesOutboxWorker(MesClient client, ISqlSugarClient db, MesOptions options, ILogger<MesOutboxWorker> logger) : BackgroundService
{
    /// <summary>发件箱发生变化时触发。</summary>
    public event EventHandler? OutboxChanged;

    /// <summary>执行。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.OutboxIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "MES 补传异常");
            }
            await Task.Delay(interval, stoppingToken);
        }
    }

    /// <summary>刷出。</summary>
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        var pending = await db.Queryable<MesOutboxEntity>()
            .Where(o => o.Status == MesOutboxStatus.Pending)
            .OrderBy(o => o.Id)
            .Take(50)
            .ToListAsync(ct);
        var sent = 0;
        foreach (var item in pending)
        {
            var r = await client.ResendAsync(item.InterfaceCode, item.Payload, ct);
            item.RetryCount++;
            item.UpdatedAt = DateTime.Now;
            if (r.Success)
            {
                item.Status = MesOutboxStatus.Sent;
                item.SentAt = DateTime.Now;
                sent++;
            }
            else
            {
                item.LastError = r.Message;
                if (item.RetryCount >= options.OutboxMaxRetry) item.Status = MesOutboxStatus.Failed;
            }
            await db.Updateable(item).ExecuteCommandAsync(ct);
            if (r.Offline) break;
        }
        if (sent > 0)
        {
            logger.LogInformation("MES 补传成功 {Count} 条", sent);
            OutboxChanged?.Invoke(this, EventArgs.Empty);
        }
        return sent;
    }

    /// <summary>待处理数量。</summary>
    public Task<int> PendingCountAsync() => db.Queryable<MesOutboxEntity>().CountAsync(o => o.Status == MesOutboxStatus.Pending);
}
