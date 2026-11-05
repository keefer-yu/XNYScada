using SqlSugar;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Production;

public sealed class ProductionService(ISqlSugarClient db, ICurrentUser currentUser, IAlarmService alarms, Lazy<RecipeService> recipes) : IProductionService
{
    private readonly object _sync = new();

    public event EventHandler? CountersChanged;

    public event EventHandler<CellRecordEntity>? RecordSaved;

    public ProductionCounters Counters { get; private set; } = new();

    public async Task SaveCellRecordAsync(CellRecordEntity record)
    {
        record.Id = SnowFlakeSingle.Instance.NextId();
        record.CreateTime = DateTime.Now;
        record.Operator ??= currentUser.IsAuthenticated ? currentUser.UserName : null;
        await db.Insertable(record).SplitTable().ExecuteCommandAsync();

        int? fullBin = null;
        lock (_sync)
        {
            Counters.Total++;
            if (record.IsOk) Counters.Ok++;
            else Counters.Ng++;
            if (record.BinNo is { } bin)
            {
                Counters.BinCounts[bin] = Counters.BinCounts.GetValueOrDefault(bin) + 1;
                var capacity = recipes.Value.GetBoxCapacity(bin);
                if (capacity > 0 && Counters.BinCounts[bin] >= capacity) fullBin = bin;
            }
            if (!record.IsOk && !string.IsNullOrEmpty(record.NgCode))
                Counters.NgCounts[record.NgCode] = Counters.NgCounts.GetValueOrDefault(record.NgCode) + 1;
        }
        if (fullBin != null) await alarms.RaiseAsync($"BinFull:{fullBin}", $"档位 {fullBin} 已满箱，请换箱后清零计数", Domain.AlarmLevel.Warning);
        CountersChanged?.Invoke(this, EventArgs.Empty);
        RecordSaved?.Invoke(this, record);
    }

    public async Task SaveNgRecordAsync(NgRecordEntity record)
    {
        record.Id = SnowFlakeSingle.Instance.NextId();
        record.CreateTime = DateTime.Now;
        await db.Insertable(record).SplitTable().ExecuteCommandAsync();
    }

    public async Task<bool> CellProcessedAsync(string cellCode, int lookbackDays = 30) =>
        await db.Queryable<CellRecordEntity>()
            .SplitTable(DateTime.Now.AddDays(-lookbackDays), DateTime.Now.AddDays(1))
            .AnyAsync(r => r.CellCode == cellCode);

    public void ResetCounters()
    {
        lock (_sync) Counters = new ProductionCounters();
        CountersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetBin(int binNo)
    {
        lock (_sync) Counters.BinCounts.Remove(binNo);
        _ = alarms.ClearAsync($"BinFull:{binNo}");
        CountersChanged?.Invoke(this, EventArgs.Empty);
    }
}
