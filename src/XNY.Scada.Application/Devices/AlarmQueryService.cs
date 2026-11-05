using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Application.Devices;

public sealed class AlarmQueryService(ISqlSugarClient db)
{
    public Task<List<AlarmRecordEntity>> QueryAsync(DateTime from, DateTime to, string? keyword = null, int limit = 2000) =>
        db.Queryable<AlarmRecordEntity>()
            .Where(a => a.RaisedAt >= from && a.RaisedAt <= to)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), a => a.Message.Contains(keyword!) || a.Source.Contains(keyword!))
            .OrderBy(a => a.Id, OrderByType.Desc)
            .Take(limit)
            .ToListAsync();
}
