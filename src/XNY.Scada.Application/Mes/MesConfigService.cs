using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Mes;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Mes;

/// <summary>MES配置服务。</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="client">客户区。</param>
/// <param name="outbox">发件箱。</param>
/// <param name="permissions">权限。</param>
/// <param name="audit">审计。</param>
public sealed class MesConfigService(
    ISqlSugarClient db,
    IMesClient client,
    MesOutboxWorker outbox,
    IPermissionService permissions,
    IAuditService audit)
{
    /// <summary>列出。</summary>
    public Task<List<MesInterfaceEntity>> ListAsync() => db.Queryable<MesInterfaceEntity>().OrderBy(m => m.Code).ToListAsync();

    /// <summary>保存。</summary>
    public async Task<MesInterfaceEntity> SaveAsync(MesInterfaceEntity entity, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Mes.Edit);
        if (string.IsNullOrWhiteSpace(entity.Code)) throw new InvalidOperationException("接口编码不能为空");
        _ = Json.Deserialize<Dictionary<string, string>>(entity.HeadersJson);
        _ = Json.Deserialize<Dictionary<string, string>>(entity.ResponseMapJson);
        var existing = await db.Queryable<MesInterfaceEntity>().FirstAsync(m => m.Code == entity.Code && m.Id != entity.Id);
        if (existing != null) throw new InvalidOperationException($"接口编码 {entity.Code} 已存在");
        string? old = null;
        if (entity.Id == 0)
        {
            entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        }
        else
        {
            old = Json.Serialize(await db.Queryable<MesInterfaceEntity>().InSingleAsync(entity.Id));
            entity.UpdatedAt = DateTime.Now;
            await db.Updateable(entity).IgnoreColumns(m => m.CreatedAt).ExecuteCommandAsync();
        }
        if (checkPermission) await audit.LogAsync(AuditActions.MesChange, $"mes:{entity.Code}", old, Json.Serialize(entity));
        await client.ReloadAsync();
        return entity;
    }

    /// <summary>删除。</summary>
    public async Task DeleteAsync(long id)
    {
        permissions.Demand(Permissions.Mes.Edit);
        var e = await db.Queryable<MesInterfaceEntity>().InSingleAsync(id);
        await db.Deleteable<MesInterfaceEntity>().In(id).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.MesChange, $"mes:{e?.Code}", "存在", "已删除");
        await client.ReloadAsync();
    }

    /// <summary>测试。</summary>
    public Task<MesResult> TestAsync(MesInterfaceEntity entity, string argsJson)
    {
        permissions.Demand(Permissions.Mes.Test);
        return client.TestAsync(entity, Json.ToDictionary(argsJson));
    }

    /// <summary>获取日志。</summary>
    public Task<List<MesCallLogEntity>> GetLogsAsync(string? code, DateTime from, DateTime to, int limit = 500) =>
        db.Queryable<MesCallLogEntity>()
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to)
            .WhereIF(!string.IsNullOrWhiteSpace(code), l => l.InterfaceCode == code)
            .OrderBy(l => l.Id, OrderByType.Desc)
            .Take(limit)
            .ToListAsync();

    /// <summary>获取发件箱。</summary>
    public Task<List<MesOutboxEntity>> GetOutboxAsync(MesOutboxStatus? status = MesOutboxStatus.Pending) =>
        db.Queryable<MesOutboxEntity>()
            .WhereIF(status != null, o => o.Status == status)
            .OrderBy(o => o.Id, OrderByType.Desc)
            .Take(1000)
            .ToListAsync();

    /// <summary>刷出发件箱。</summary>
    public Task<int> FlushOutboxAsync() => outbox.FlushAsync();

    /// <summary>重新排队失败。</summary>
    public async Task RequeueFailedAsync()
    {
        permissions.Demand(Permissions.Mes.Edit);
        await db.Updateable<MesOutboxEntity>()
            .SetColumns(o => new MesOutboxEntity { Status = MesOutboxStatus.Pending, RetryCount = 0 })
            .Where(o => o.Status == MesOutboxStatus.Failed)
            .ExecuteCommandAsync();
    }
}
