using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;

namespace XNY.Scada.Device.Runtime;

/// <summary>报警服务：既接受流程/业务主动报警，也根据点位报警定义自动监控</summary>
public sealed class AlarmService : IAlarmService, IDisposable
{
    /// <summary>数据库。</summary>
    private readonly ISqlSugarClient _db;
    /// <summary>点位。</summary>
    private readonly ITagService _tags;
    /// <summary>日志。</summary>
    private readonly ILogger<AlarmService> _logger;
    /// <summary>活动。</summary>
    private readonly ConcurrentDictionary<string, AlarmRecordEntity> _active = new();
    /// <summary>定义。</summary>
    private ILookup<string, AlarmDefinitionEntity> _definitions = Array.Empty<AlarmDefinitionEntity>().ToLookup(d => d.TagName);

    /// <summary>创建报警服务。</summary>
    public AlarmService(ISqlSugarClient db, ITagService tags, ILogger<AlarmService> logger)
    {
        _db = db;
        _tags = tags;
        _logger = logger;
        _tags.TagChanged += OnTagChanged;
    }

    /// <summary>报警发生变化时触发。</summary>
    public event EventHandler? AlarmsChanged;

    /// <summary>活动报警。</summary>
    public IReadOnlyList<AlarmRecordEntity> ActiveAlarms => _active.Values.OrderByDescending(a => a.RaisedAt).ToList();

    /// <summary>加载。</summary>
    public async Task LoadAsync()
    {
        var defs = await _db.Queryable<AlarmDefinitionEntity>().Where(d => d.Enabled).ToListAsync();
        _definitions = defs.ToLookup(d => d.TagName, StringComparer.OrdinalIgnoreCase);
        var active = await _db.Queryable<AlarmRecordEntity>().Where(a => a.IsActive).ToListAsync();
        _active.Clear();
        foreach (var a in active) _active[a.Source] = a;
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>引发。</summary>
    public async Task RaiseAsync(string source, string message, AlarmLevel level)
    {
        if (_active.ContainsKey(source)) return;
        var record = new AlarmRecordEntity { Source = source, Message = message, Level = level, RaisedAt = DateTime.Now, IsActive = true };
        if (!_active.TryAdd(source, record)) return;
        record.Id = await _db.Insertable(record).ExecuteReturnBigIdentityAsync();
        _logger.LogWarning("报警 [{Level}] {Source}: {Message}", level, source, message);
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>清空。</summary>
    public async Task ClearAsync(string source)
    {
        if (!_active.TryRemove(source, out var record)) return;
        record.IsActive = false;
        record.ClearedAt = DateTime.Now;
        await _db.Updateable(record).ExecuteCommandAsync();
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>确认。</summary>
    public async Task AcknowledgeAsync(long alarmId, string user)
    {
        var record = _active.Values.FirstOrDefault(a => a.Id == alarmId)
                     ?? await _db.Queryable<AlarmRecordEntity>().InSingleAsync(alarmId);
        if (record == null) return;
        record.AcknowledgedAt = DateTime.Now;
        record.AcknowledgedBy = user;
        await _db.Updateable(record).ExecuteCommandAsync();
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>当标记变化时更新关联状态。</summary>
    private void OnTagChanged(object? sender, TagChangedEventArgs e)
    {
        if (!_definitions.Contains(e.Tag.FullName)) return;
        foreach (var def in _definitions[e.Tag.FullName])
        {
            var source = $"Tag:{def.TagName}:{def.Kind}";
            var active = e.NewValue.Quality == TagQuality.Good && Evaluate(def, e.NewValue.Value);
            _ = active ? RaiseAsync(source, def.Message, def.Level) : ClearAsync(source);
        }
    }

    /// <summary>求值。</summary>
    public static bool Evaluate(AlarmDefinitionEntity def, object? value)
    {
        if (def.Kind == AlarmKind.Bit) return TagValueConverter.ToBool(value);
        if (value == null || !double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return false;
        return def.Kind switch
        {
            AlarmKind.High => v > def.Threshold,
            AlarmKind.Low => v < def.Threshold,
            AlarmKind.Equal => Math.Abs(v - def.Threshold) < 1e-9,
            _ => false
        };
    }

    /// <summary>释放资源。</summary>
    public void Dispose() => _tags.TagChanged -= OnTagChanged;
}
