using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;

namespace XNY.Scada.Application.Production;

/// <summary>托盘服务。</summary>
/// <param name="db">数据库客户端。</param>
public sealed class TrayService(ISqlSugarClient db) : ITrayService
{
    /// <summary>锁定。</summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>托盘发生变化时触发。</summary>
    public event EventHandler<string>? TrayChanged;

    /// <summary>当前托盘代码。</summary>
    public string? CurrentTrayCode { get; private set; }

    /// <summary>获取或创建托盘。</summary>
    public async Task<TrayEntity> GetOrCreateTrayAsync(string trayCode, int rows, int cols)
    {
        if (string.IsNullOrWhiteSpace(trayCode)) throw new ArgumentException("托盘号不能为空");
        await _lock.WaitAsync();
        try
        {
            var tray = await db.Queryable<TrayEntity>().FirstAsync(t => t.TrayCode == trayCode);
            if (tray == null)
            {
                tray = new TrayEntity { TrayCode = trayCode, Rows = Math.Max(1, rows), Cols = Math.Max(1, cols), Status = TrayStatus.Loading };
                tray.Id = await db.Insertable(tray).ExecuteReturnBigIdentityAsync();
            }
            else if (tray.Status is TrayStatus.Empty or TrayStatus.Uploaded && tray.CellCount == 0)
            {
                tray.Status = TrayStatus.Loading;
                tray.Rows = Math.Max(1, rows);
                tray.Cols = Math.Max(1, cols);
                await db.Updateable(tray).ExecuteCommandAsync();
            }
            CurrentTrayCode = trayCode;
            return tray;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>获取托盘。</summary>
    public Task<TrayEntity?> GetTrayAsync(string trayCode) => db.Queryable<TrayEntity>().FirstAsync(t => t.TrayCode == trayCode)!;

    /// <summary>绑定单元格。</summary>
    public async Task<BindResult> BindCellAsync(string trayCode, int position, string cellCode)
    {
        await _lock.WaitAsync();
        try
        {
            var tray = await db.Queryable<TrayEntity>().FirstAsync(t => t.TrayCode == trayCode);
            if (tray == null) return new BindResult(false, $"托盘 {trayCode} 不存在", 0, false);
            if (tray.Status != TrayStatus.Loading) return new BindResult(false, $"托盘 {trayCode} 状态为 {tray.Status}，不能绑定", tray.CellCount, false);
            var capacity = tray.Rows * tray.Cols;
            if (position < 1 || position > capacity) return new BindResult(false, $"位置 {position} 超出托盘范围 1~{capacity}", tray.CellCount, false);
            if (string.IsNullOrWhiteSpace(cellCode)) return new BindResult(false, "电芯码为空", tray.CellCount, false);

            var existing = await db.Queryable<TrayCellEntity>().FirstAsync(c => c.CellCode == cellCode && c.Active);
            if (existing != null) return new BindResult(false, $"电芯 {cellCode} 已绑定托盘 {existing.TrayCode} 位置 {existing.Position}", tray.CellCount, false);
            if (await db.Queryable<TrayCellEntity>().AnyAsync(c => c.TrayCode == trayCode && c.Position == position && c.Active))
                return new BindResult(false, $"托盘 {trayCode} 位置 {position} 已有电芯", tray.CellCount, false);

            await db.Insertable(new TrayCellEntity { TrayCode = trayCode, Position = position, CellCode = cellCode }).ExecuteCommandAsync();
            tray.CellCount++;
            var full = tray.CellCount >= capacity;
            if (full)
            {
                tray.Status = TrayStatus.Full;
                tray.CompletedAt = DateTime.Now;
            }
            tray.UpdatedAt = DateTime.Now;
            await db.Updateable(tray).ExecuteCommandAsync();
            TrayChanged?.Invoke(this, trayCode);
            return new BindResult(true, full ? "托盘已满" : "绑定成功", tray.CellCount, full);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>获取单元格。</summary>
    public async Task<IReadOnlyList<TrayCellEntity>> GetCellsAsync(string trayCode) =>
        await db.Queryable<TrayCellEntity>().Where(c => c.TrayCode == trayCode && c.Active).OrderBy(c => c.Position).ToListAsync();

    /// <summary>解绑。</summary>
    public async Task<int> UnbindAsync(string trayCode, int? position)
    {
        await _lock.WaitAsync();
        try
        {
            var n = await db.Updateable<TrayCellEntity>()
                .SetColumns(c => new TrayCellEntity { Active = false, UnbindTime = DateTime.Now })
                .Where(c => c.TrayCode == trayCode && c.Active)
                .WhereIF(position != null, c => c.Position == position)
                .ExecuteCommandAsync();
            var tray = await db.Queryable<TrayEntity>().FirstAsync(t => t.TrayCode == trayCode);
            if (tray != null)
            {
                tray.CellCount = Math.Max(0, tray.CellCount - n);
                tray.Status = tray.CellCount == 0 ? TrayStatus.Empty : TrayStatus.Unloading;
                tray.UpdatedAt = DateTime.Now;
                await db.Updateable(tray).ExecuteCommandAsync();
            }
            CurrentTrayCode = trayCode;
            TrayChanged?.Invoke(this, trayCode);
            return n;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>完成托盘。</summary>
    public async Task CompleteTrayAsync(string trayCode)
    {
        await db.Updateable<TrayEntity>()
            .SetColumns(t => new TrayEntity { Status = TrayStatus.Uploaded, CompletedAt = DateTime.Now, UpdatedAt = DateTime.Now })
            .Where(t => t.TrayCode == trayCode)
            .ExecuteCommandAsync();
        TrayChanged?.Invoke(this, trayCode);
    }

    /// <summary>查找托盘的单元格。</summary>
    public async Task<string?> FindTrayOfCellAsync(string cellCode) =>
        (await db.Queryable<TrayCellEntity>().FirstAsync(c => c.CellCode == cellCode && c.Active))?.TrayCode;
}
