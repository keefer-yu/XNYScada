using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Application.Flows;

/// <summary>流日志查询服务。</summary>
/// <param name="db">数据库客户端。</param>
public sealed class FlowLogQueryService(ISqlSugarClient db)
{
    /// <summary>查询。</summary>
    public Task<List<FlowInstanceLogEntity>> QueryAsync(string? flowCode, DateTime from, DateTime to, FlowInstanceStatus? status = null, int limit = 500) =>
        db.Queryable<FlowInstanceLogEntity>()
            .Where(l => l.StartedAt >= from && l.StartedAt <= to)
            .WhereIF(!string.IsNullOrWhiteSpace(flowCode), l => l.FlowCode == flowCode)
            .WhereIF(status != null, l => l.Status == status)
            .OrderBy(l => l.Id, OrderByType.Desc)
            .Take(limit)
            .ToListAsync();
}
