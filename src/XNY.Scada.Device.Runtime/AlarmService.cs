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
    private readonly ISqlSugarClient _db;
    private readonly ITagService _tags;
    private readonly ILogger<AlarmService> _logger;
    private readonly ConcurrentDictionary<string, AlarmRecordEntity> _active = new();
    private ILookup<string, AlarmDefinitionEntity> _definitions = Array.Empty<AlarmDefinitionEntity>().ToLookup(d => d.TagName);

    public AlarmService(ISqlSugarClient db, ITagService tags, ILogger<AlarmService> logger)
    {
        _db = db;
        _tags = tags;
        _logger = logger;
        _tags.TagChanged += OnTagChanged;
    }

    public event EventHandler? AlarmsChanged;

    public IReadOnlyList<AlarmRecordEntity> ActiveAlarms => _active.Values.OrderByDescending(a => a.RaisedAt).ToList();

    public async Task LoadAsync()
    {
        var defs = await _db.Queryable<AlarmDefinitionEntity>().Where(d => d.Enabled).ToListAsync();
        _definitions = defs.ToLookup(d => d.TagName, StringComparer.OrdinalIgnoreCase);
        var active = await _db.Queryable<AlarmRecordEntity>().Where(a => a.IsActive).ToListAsync();
        _active.Clear();
        foreach (var a in active) _active[a.Source] = a;
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RaiseAsync(string source, string message, AlarmLevel level)
    {
        if (_active.ContainsKey(source)) return;
        var record = new AlarmRecordEntity { Source = source, Message = message, Level = level, RaisedAt = DateTime.Now, IsActive = true };
        if (!_active.TryAdd(source, record)) return;
        record.Id = await _db.Insertable(record).ExecuteReturnBigIdentityAsync();
        _logger.LogWarning("报警 [{Level}] {Source}: {Message}", level, source, message);
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync(string source)
    {
        if (!_active.TryRemove(source, out var record)) return;
        record.IsActive = false;
        record.ClearedAt = DateTime.Now;
        await _db.Updateable(record).ExecuteCommandAsync();
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

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

    public void Dispose() => _tags.TagChanged -= OnTagChanged;
}
