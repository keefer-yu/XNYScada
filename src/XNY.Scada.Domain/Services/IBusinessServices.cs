using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Domain.Services;

public sealed record BindResult(bool Success, string Message, int CellCount, bool TrayFull);

public interface ITrayService
{
    event EventHandler<string>? TrayChanged;

    Task<TrayEntity> GetOrCreateTrayAsync(string trayCode, int rows, int cols);

    Task<TrayEntity?> GetTrayAsync(string trayCode);

    Task<BindResult> BindCellAsync(string trayCode, int position, string cellCode);

    Task<IReadOnlyList<TrayCellEntity>> GetCellsAsync(string trayCode);

    /// <summary>解绑电芯，position 为 null 时解绑整盘</summary>
    Task<int> UnbindAsync(string trayCode, int? position);

    Task CompleteTrayAsync(string trayCode);

    Task<string?> FindTrayOfCellAsync(string cellCode);

    string? CurrentTrayCode { get; }
}

public sealed record GradeResult(bool Matched, int BinNo, string BinName, bool IsNg, string? NgCode, string? Message);

public interface IGradingService
{
    Task<GradeResult> GradeAsync(IReadOnlyDictionary<string, object?> values, string? recipeCode = null);
}

public sealed class ProductionCounters
{
    public int Total { get; set; }
    public int Ok { get; set; }
    public int Ng { get; set; }
    public Dictionary<int, int> BinCounts { get; } = new();
    public Dictionary<string, int> NgCounts { get; } = new();
    public DateTime ShiftStart { get; set; } = DateTime.Now;
    public double Yield => Total == 0 ? 0 : (double)Ok / Total;
}

public interface IProductionService
{
    event EventHandler? CountersChanged;

    event EventHandler<CellRecordEntity>? RecordSaved;

    ProductionCounters Counters { get; }

    Task SaveCellRecordAsync(CellRecordEntity record);

    Task SaveNgRecordAsync(NgRecordEntity record);

    Task<bool> CellProcessedAsync(string cellCode, int lookbackDays = 30);

    void ResetCounters();

    /// <summary>清空某档位计数（换箱后）</summary>
    void ResetBin(int binNo);
}

public interface IAlarmService
{
    event EventHandler? AlarmsChanged;

    Task RaiseAsync(string source, string message, AlarmLevel level);

    Task ClearAsync(string source);

    Task AcknowledgeAsync(long alarmId, string user);

    IReadOnlyList<AlarmRecordEntity> ActiveAlarms { get; }
}

public sealed record RecipeSnapshot(long Id, string Code, string Name, string ProductModel, int Version, IReadOnlyDictionary<string, object?> Params);

public interface IRecipeProvider
{
    event EventHandler? CurrentRecipeChanged;

    RecipeSnapshot? Current { get; }

    object? GetParam(string name);
}
