using System.Collections.Concurrent;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Settings;

/// <summary>主界面布局（由机型模板决定）</summary>
/// <summary>主界面布局：托盘矩阵、档位、NG 料盒，以及要显示的关键点位和可手动触发的流程。</summary>
public sealed class MachineLayout
{
    /// <summary>是否主界面是否显示托盘。</summary>
    public bool ShowTray { get; set; }
    /// <summary>托盘行数。</summary>
    public int TrayRows { get; set; } = 4;
    /// <summary>托盘列数。</summary>
    public int TrayCols { get; set; } = 6;
    /// <summary>是否主界面是否显示档位。</summary>
    public bool ShowBins { get; set; }
    /// <summary>档位数量。</summary>
    public int BinCount { get; set; } = 8;
    /// <summary>是否主界面是否显示 NG 料盒。</summary>
    public bool ShowNg { get; set; }
    /// <summary>NG 料盒数量。</summary>
    public int NgBoxCount { get; set; } = 4;
    /// <summary>主界面显示的关键点位</summary>
    public List<string> KeyTags { get; set; } = [];
    /// <summary>可手动触发的流程</summary>
    public List<string> ManualFlows { get; set; } = [];
}

/// <summary>系统参数服务。</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="permissions">权限。</param>
/// <param name="audit">审计。</param>
public sealed class SystemParamService(ISqlSugarClient db, IPermissionService permissions, IAuditService audit) : IFlowEnvironment
{
    /// <summary>缓存。</summary>
    private ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>参数发生变化时触发。</summary>
    public event EventHandler<string>? ParamChanged;

    /// <summary>加载。</summary>
    public async Task LoadAsync()
    {
        var list = await db.Queryable<SystemParamEntity>().ToListAsync();
        _cache = new ConcurrentDictionary<string, string>(list.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>获取。</summary>
    public string Get(string key, string defaultValue = "") => _cache.TryGetValue(key, out var v) ? v : defaultValue;

    /// <summary>获取整数。</summary>
    public int GetInt(string key, int defaultValue) => int.TryParse(Get(key), out var v) ? v : defaultValue;

    /// <summary>获取双。</summary>
    public double GetDouble(string key, double defaultValue) =>
        double.TryParse(Get(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : defaultValue;

    /// <summary>机型编码。</summary>
    public string MachineType => Get("MachineType", MachineTypes.Grading);

    /// <summary>工位。</summary>
    public string Station => Get("Station", "ST01");

    /// <summary>布局。</summary>
    public MachineLayout Layout => Json.Deserialize<MachineLayout>(Get("LayoutJson", "{}")) ?? new MachineLayout();

    /// <summary>获取参数。</summary>
    public object? GetParam(string name) => _cache.TryGetValue(name, out var v) ? v : null;

    /// <summary>列出。</summary>
    public Task<List<SystemParamEntity>> ListAsync() => db.Queryable<SystemParamEntity>().OrderBy(p => p.Group).OrderBy(p => p.Key).ToListAsync();

    /// <summary>设置。</summary>
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

    /// <summary>删除。</summary>
    public async Task DeleteAsync(string key)
    {
        permissions.Demand(Permissions.System.Edit);
        if (DatabaseDefaults.Contains(key)) throw new ScadaSecurityException("系统内置参数不可删除");
        await db.Deleteable<SystemParamEntity>().Where(p => p.Key == key).ExecuteCommandAsync();
        _cache.TryRemove(key, out _);
        await audit.LogAsync(AuditActions.ParamChange, $"param:{key}", "存在", "已删除");
        ParamChanged?.Invoke(this, key);
    }

    /// <summary>数据库默认值。</summary>
    private static readonly HashSet<string> DatabaseDefaults = new(Data.DatabaseInitializer.DefaultParams.Keys, StringComparer.OrdinalIgnoreCase);
}
