using System.Collections.Concurrent;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Settings;

/// <summary>主界面布局（由机型模板决定）</summary>
public sealed class MachineLayout
{
    public bool ShowTray { get; set; }
    public int TrayRows { get; set; } = 4;
    public int TrayCols { get; set; } = 6;
    public bool ShowBins { get; set; }
    public int BinCount { get; set; } = 8;
    public bool ShowNg { get; set; }
    public int NgBoxCount { get; set; } = 4;
    /// <summary>主界面显示的关键点位</summary>
    public List<string> KeyTags { get; set; } = [];
    /// <summary>可手动触发的流程</summary>
    public List<string> ManualFlows { get; set; } = [];
}

public sealed class SystemParamService(ISqlSugarClient db, IPermissionService permissions, IAuditService audit) : IFlowEnvironment
{
    private ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? ParamChanged;

    public async Task LoadAsync()
    {
        var list = await db.Queryable<SystemParamEntity>().ToListAsync();
        _cache = new ConcurrentDictionary<string, string>(list.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    public string Get(string key, string defaultValue = "") => _cache.TryGetValue(key, out var v) ? v : defaultValue;

    public int GetInt(string key, int defaultValue) => int.TryParse(Get(key), out var v) ? v : defaultValue;

    public double GetDouble(string key, double defaultValue) =>
        double.TryParse(Get(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : defaultValue;

    public string MachineType => Get("MachineType", MachineTypes.Grading);

    public string Station => Get("Station", "ST01");

    public MachineLayout Layout => Json.Deserialize<MachineLayout>(Get("LayoutJson", "{}")) ?? new MachineLayout();

    public object? GetParam(string name) => _cache.TryGetValue(name, out var v) ? v : null;

    public Task<List<SystemParamEntity>> ListAsync() => db.Queryable<SystemParamEntity>().OrderBy(p => p.Group).OrderBy(p => p.Key).ToListAsync();

    public async Task SetAsync(string key, string value, string? group = null, string? description = null, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.System.Edit);
        var p = await db.Queryable<SystemParamEntity>().FirstAsync(x => x.Key == key);
        var old = p?.Value;
        if (p == null)
        {
            await db.Insertable(new SystemParamEntity { Key = key, Value = value, Group = group ?? "Custom", Description = description }).ExecuteCommandAsync();
        }
        else
        {
            if (old == value) return;
            p.Value = value;
            p.UpdatedAt = DateTime.Now;
            await db.Updateable(p).ExecuteCommandAsync();
        }
        _cache[key] = value;
        await audit.LogAsync(AuditActions.ParamChange, $"param:{key}", old, value);
        ParamChanged?.Invoke(this, key);
    }

    public async Task DeleteAsync(string key)
    {
        permissions.Demand(Permissions.System.Edit);
        if (DatabaseDefaults.Contains(key)) throw new ScadaSecurityException("系统内置参数不可删除");
        await db.Deleteable<SystemParamEntity>().Where(p => p.Key == key).ExecuteCommandAsync();
        _cache.TryRemove(key, out _);
        await audit.LogAsync(AuditActions.ParamChange, $"param:{key}", "存在", "已删除");
        ParamChanged?.Invoke(this, key);
    }

    private static readonly HashSet<string> DatabaseDefaults = new(Data.DatabaseInitializer.DefaultParams.Keys, StringComparer.OrdinalIgnoreCase);
}
