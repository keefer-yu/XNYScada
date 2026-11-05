using System.Globalization;
using System.Text;
using SqlSugar;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Production;

public sealed record ProductionQuery(DateTime From, DateTime To, string? CellCode = null, string? TrayCode = null, bool? IsOk = null, int Limit = 5000);

public sealed record HourlyOutput(DateTime Hour, int Total, int Ok, int Ng);

public sealed record OeeSnapshot(double Availability, double Performance, double Quality, double Oee, double Uph);

public sealed class ProductionQueryService(
    ISqlSugarClient db,
    IPermissionService permissions,
    IAuditService audit,
    IProductionService production)
{
    public Task<List<CellRecordEntity>> QueryAsync(ProductionQuery q) =>
        db.Queryable<CellRecordEntity>()
            .SplitTable(q.From, q.To)
            .Where(r => r.CreateTime >= q.From && r.CreateTime <= q.To)
            .WhereIF(!string.IsNullOrWhiteSpace(q.CellCode), r => r.CellCode.Contains(q.CellCode!))
            .WhereIF(!string.IsNullOrWhiteSpace(q.TrayCode), r => r.TrayCode == q.TrayCode)
            .WhereIF(q.IsOk != null, r => r.IsOk == q.IsOk)
            .OrderBy(r => r.CreateTime, OrderByType.Desc)
            .Take(q.Limit)
            .ToListAsync();

    public Task<List<NgRecordEntity>> QueryNgAsync(DateTime from, DateTime to, string? cellCode = null) =>
        db.Queryable<NgRecordEntity>()
            .SplitTable(from, to)
            .Where(r => r.CreateTime >= from && r.CreateTime <= to)
            .WhereIF(!string.IsNullOrWhiteSpace(cellCode), r => r.CellCode.Contains(cellCode!))
            .OrderBy(r => r.CreateTime, OrderByType.Desc)
            .Take(5000)
            .ToListAsync();

    public async Task<List<HourlyOutput>> HourlyAsync(DateTime from, DateTime to)
    {
        var rows = await db.Queryable<CellRecordEntity>().SplitTable(from, to)
            .Where(r => r.CreateTime >= from && r.CreateTime <= to)
            .Select(r => new { r.CreateTime, r.IsOk })
            .ToListAsync();
        return rows.GroupBy(r => new DateTime(r.CreateTime.Year, r.CreateTime.Month, r.CreateTime.Day, r.CreateTime.Hour, 0, 0))
            .OrderBy(g => g.Key)
            .Select(g => new HourlyOutput(g.Key, g.Count(), g.Count(x => x.IsOk), g.Count(x => !x.IsOk)))
            .ToList();
    }

    /// <summary>
    /// 简化 OEE：可用率 = 1 - 班次内严重报警停机时长 / 班次时长；性能 = 产量 × 理想节拍 / 运行时长；质量 = 良率
    /// </summary>
    public async Task<OeeSnapshot> OeeAsync(double idealCycleSeconds)
    {
        var c = production.Counters;
        var shiftStart = c.ShiftStart;
        var elapsed = Math.Max(1, (DateTime.Now - shiftStart).TotalSeconds);
        var downAlarms = await db.Queryable<AlarmRecordEntity>()
            .Where(a => a.RaisedAt >= shiftStart && (a.Level == Domain.AlarmLevel.Error || a.Level == Domain.AlarmLevel.Critical))
            .ToListAsync();
        var down = downAlarms.Sum(a => ((a.ClearedAt ?? DateTime.Now) - a.RaisedAt).TotalSeconds);
        var run = Math.Max(1, elapsed - down);
        var availability = Math.Clamp(run / elapsed, 0, 1);
        var performance = idealCycleSeconds <= 0 ? 1 : Math.Clamp(c.Total * idealCycleSeconds / run, 0, 1);
        var quality = c.Total == 0 ? 1 : c.Yield;
        return new OeeSnapshot(availability, performance, quality, availability * performance * quality, c.Total / (elapsed / 3600));
    }

    public async Task<int> ExportCsvAsync(ProductionQuery q, string path)
    {
        permissions.Demand(Permissions.Production.Export);
        var rows = await QueryAsync(q with { Limit = 1_000_000 });
        var sb = new StringBuilder();
        sb.AppendLine("时间,电芯码,托盘号,位置,电压,内阻,容量,档位,档位名,结果,NG代码,工位,配方,操作员,扩展");
        foreach (var r in rows.OrderBy(r => r.CreateTime))
        {
            sb.AppendLine(string.Join(",", new object?[]
            {
                r.CreateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), r.CellCode, r.TrayCode, r.Position,
                Fmt(r.Voltage), Fmt(r.InternalResistance), Fmt(r.Capacity), r.BinNo, r.BinName, r.IsOk ? "OK" : "NG", r.NgCode,
                r.Station, r.RecipeCode, r.Operator, r.ExtraJson
            }.Select(Csv)));
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
        await audit.LogAsync(AuditActions.Production, "export", newValue: $"{rows.Count} 条 → {path}");
        return rows.Count;
    }

    private static string? Fmt(double? v) => v?.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Csv(object? v)
    {
        var s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
