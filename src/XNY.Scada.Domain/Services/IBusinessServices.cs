using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Domain.Services;

/// <summary>绑定结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="Message">消息。</param>
/// <param name="CellCount">电芯数量。</param>
/// <param name="TrayFull">托盘整行。</param>
public sealed record BindResult(bool Success, string Message, int CellCount, bool TrayFull);

/// <summary>I托盘服务。</summary>
public interface ITrayService
{
    /// <summary>托盘发生变化时触发。</summary>
    event EventHandler<string>? TrayChanged;

    /// <summary>获取或创建托盘。</summary>
    Task<TrayEntity> GetOrCreateTrayAsync(string trayCode, int rows, int cols);

    /// <summary>获取托盘。</summary>
    Task<TrayEntity?> GetTrayAsync(string trayCode);

    /// <summary>绑定单元格。</summary>
    Task<BindResult> BindCellAsync(string trayCode, int position, string cellCode);

    /// <summary>获取单元格。</summary>
    Task<IReadOnlyList<TrayCellEntity>> GetCellsAsync(string trayCode);

    /// <summary>解绑电芯，position 为 null 时解绑整盘</summary>
    Task<int> UnbindAsync(string trayCode, int? position);

    /// <summary>完成托盘。</summary>
    Task CompleteTrayAsync(string trayCode);

    /// <summary>查找托盘的单元格。</summary>
    Task<string?> FindTrayOfCellAsync(string cellCode);

    /// <summary>当前托盘代码。</summary>
    string? CurrentTrayCode { get; }
}

/// <summary>分档结果。</summary>
/// <param name="Matched">已匹配。</param>
/// <param name="BinNo">档位号。</param>
/// <param name="BinName">档位名称。</param>
/// <param name="IsNg">是否判定为 NG。</param>
/// <param name="NgCode">NG 代码。</param>
/// <param name="Message">消息。</param>
public sealed record GradeResult(bool Matched, int BinNo, string BinName, bool IsNg, string? NgCode, string? Message);

/// <summary>I分档服务。</summary>
public interface IGradingService
{
    /// <summary>分档。</summary>
    Task<GradeResult> GradeAsync(IReadOnlyDictionary<string, object?> values, string? recipeCode = null);
}

/// <summary>生产计数器。</summary>
public sealed class ProductionCounters
{
    /// <summary>总计。</summary>
    public int Total { get; set; }
    /// <summary>OK。</summary>
    public int Ok { get; set; }
    /// <summary>NG。</summary>
    public int Ng { get; set; }
    /// <summary>档位数量。</summary>
    public Dictionary<int, int> BinCounts { get; } = new();
    /// <summary>NG数量。</summary>
    public Dictionary<string, int> NgCounts { get; } = new();
    /// <summary>班次起点。</summary>
    public DateTime ShiftStart { get; set; } = DateTime.Now;
    /// <summary>产出。</summary>
    public double Yield => Total == 0 ? 0 : (double)Ok / Total;
}

/// <summary>I生产服务。</summary>
public interface IProductionService
{
    /// <summary>计数器发生变化时触发。</summary>
    event EventHandler? CountersChanged;

    /// <summary>记录已保存发生变化时触发。</summary>
    event EventHandler<CellRecordEntity>? RecordSaved;

    /// <summary>计数器。</summary>
    ProductionCounters Counters { get; }

    /// <summary>保存单元格记录。</summary>
    Task SaveCellRecordAsync(CellRecordEntity record);

    /// <summary>保存NG记录。</summary>
    Task SaveNgRecordAsync(NgRecordEntity record);

    /// <summary>单元格已处理。</summary>
    Task<bool> CellProcessedAsync(string cellCode, int lookbackDays = 30);

    /// <summary>重置枚举计数器。</summary>
    void ResetCounters();

    /// <summary>清空某档位计数（换箱后）</summary>
    void ResetBin(int binNo);
}

/// <summary>I报警服务。</summary>
public interface IAlarmService
{
    /// <summary>报警发生变化时触发。</summary>
    event EventHandler? AlarmsChanged;

    /// <summary>引发。</summary>
    Task RaiseAsync(string source, string message, AlarmLevel level);

    /// <summary>清空。</summary>
    Task ClearAsync(string source);

    /// <summary>确认。</summary>
    Task AcknowledgeAsync(long alarmId, string user);

    /// <summary>活动报警。</summary>
    IReadOnlyList<AlarmRecordEntity> ActiveAlarms { get; }
}

/// <summary>配方快照。</summary>
/// <param name="Id">标识。</param>
/// <param name="Code">编码。</param>
/// <param name="Name">名称。</param>
/// <param name="ProductModel">产品型号。</param>
/// <param name="Version">版本号。</param>
/// <param name="Params">参数。</param>
public sealed record RecipeSnapshot(long Id, string Code, string Name, string ProductModel, int Version, IReadOnlyDictionary<string, object?> Params);

/// <summary>配方提供程序的契约。</summary>
public interface IRecipeProvider
{
    /// <summary>当前配方发生变化时触发。</summary>
    event EventHandler? CurrentRecipeChanged;

    /// <summary>当前值。</summary>
    RecipeSnapshot? Current { get; }

    /// <summary>获取参数。</summary>
    object? GetParam(string name);
}
