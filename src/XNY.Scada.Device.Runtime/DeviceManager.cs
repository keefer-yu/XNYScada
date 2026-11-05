using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Device.Runtime;

/// <summary>设备管理器：加载设备配置、启动通讯通道、维护点位缓存，同时实现 <see cref="ITagService"/></summary>
public sealed class DeviceManager(ISqlSugarClient db, IDriverRegistry registry, ILogger<DeviceManager> logger)
    : IDeviceManager, ITagService, IAsyncDisposable
{
    private sealed class TagState(TagDefinition tag, DeviceChannel channel)
    {
        public TagDefinition Tag { get; } = tag;
        public DeviceChannel Channel { get; } = channel;
        public TagValue? Value;
    }

    private readonly SemaphoreSlim _sync = new(1, 1);
    private ConcurrentDictionary<string, TagState> _tags = new(StringComparer.OrdinalIgnoreCase);
    private List<DeviceChannel> _channels = [];

    public event EventHandler<TagChangedEventArgs>? TagChanged;

    public event EventHandler<DeviceStatusInfo>? StatusChanged;

    public bool IsRunning { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct);
        try
        {
            if (IsRunning) return;
            await LoadAsync();
            foreach (var c in _channels) c.Start();
            IsRunning = true;
            logger.LogInformation("设备管理器已启动，设备 {Devices} 台，点位 {Tags} 个", _channels.Count, _tags.Count);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task StopAsync()
    {
        await _sync.WaitAsync();
        try
        {
            await Task.WhenAll(_channels.Select(c => c.StopAsync()));
            IsRunning = false;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await StopAsync();
        await StartAsync(ct);
    }

    private async Task LoadAsync()
    {
        var devices = await db.Queryable<DeviceEntity>().ToListAsync();
        var groups = await db.Queryable<TagGroupEntity>().ToListAsync();
        var tags = await db.Queryable<TagEntity>().ToListAsync();
        var tagMap = new ConcurrentDictionary<string, TagState>(StringComparer.OrdinalIgnoreCase);
        var channels = new List<DeviceChannel>();

        foreach (var device in devices)
        {
            IPlcDriver driver;
            try
            {
                var options = Json.Deserialize<Dictionary<string, string>>(device.OptionsJson) ?? new();
                driver = registry.Create(device.DriverKey, options);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "设备 {Device} 驱动创建失败", device.Name);
                continue;
            }

            var scanGroups = groups.Where(g => g.DeviceId == device.Id)
                .Select(g => new ScanGroup(g.Name, g.ScanIntervalMs,
                    tags.Where(t => t.GroupId == g.Id).Select(t => ToDefinition(device, t)).ToList()))
                .ToList();
            var ungrouped = tags.Where(t => t.DeviceId == device.Id && groups.All(g => g.Id != t.GroupId)).ToList();
            if (ungrouped.Count > 0) scanGroups.Add(new ScanGroup("Default", 200, ungrouped.Select(t => ToDefinition(device, t)).ToList()));

            var channel = new DeviceChannel(device, driver, scanGroups, OnValue, c => StatusChanged?.Invoke(this, c.Status), logger);
            channels.Add(channel);
            foreach (var t in scanGroups.SelectMany(g => g.Tags)) tagMap[t.FullName] = new TagState(t, channel);
        }

        _tags = tagMap;
        _channels = channels;
    }

    public static TagDefinition ToDefinition(DeviceEntity device, TagEntity t) => new()
    {
        Id = t.Id,
        DeviceName = device.Name,
        Name = t.Name,
        Address = t.Address,
        DataType = t.DataType,
        Length = t.Length,
        Scale = t.Scale == 0 ? 1 : t.Scale,
        Offset = t.Offset,
        Access = t.Access
    };

    private void OnValue(TagDefinition tag, TagValue value)
    {
        if (!_tags.TryGetValue(tag.FullName, out var state)) return;
        var old = state.Value;
        state.Value = value;
        if (old != null && old.Quality == value.Quality && Equals(old.Value, value.Value)) return;
        try
        {
            TagChanged?.Invoke(this, new TagChangedEventArgs(tag, old, value));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TagChanged 处理异常 {Tag}", tag.FullName);
        }
    }

    public TagDefinition? FindTag(string fullName) => _tags.TryGetValue(fullName, out var s) ? s.Tag : null;

    public TagValue? GetValue(string fullName) => _tags.TryGetValue(fullName, out var s) ? s.Value : null;

    public IReadOnlyList<TagSnapshot> GetAll() =>
        _tags.Values.Select(s => new TagSnapshot(s.Tag, s.Value ?? TagValue.Bad())).OrderBy(s => s.Tag.FullName).ToList();

    public async Task<DeviceResult<object?>> ReadAsync(string fullName, CancellationToken ct = default)
    {
        if (!_tags.TryGetValue(fullName, out var s)) return DeviceResult<object?>.Fail($"点位不存在: {fullName}");
        if (!s.Channel.Driver.IsConnected) return DeviceResult<object?>.Fail($"设备 {s.Tag.DeviceName} 未连接");
        var r = await s.Channel.Driver.ReadAsync(s.Tag, ct);
        if (r.IsSuccess) OnValue(s.Tag, new TagValue(r.Value, TagQuality.Good, DateTime.Now));
        return r;
    }

    public async Task<DeviceResult> WriteAsync(string fullName, object? value, CancellationToken ct = default)
    {
        if (!_tags.TryGetValue(fullName, out var s)) return DeviceResult.Fail($"点位不存在: {fullName}");
        if (!s.Channel.Driver.IsConnected) return DeviceResult.Fail($"设备 {s.Tag.DeviceName} 未连接");
        var r = await s.Channel.Driver.WriteAsync(s.Tag, value, ct);
        if (r.IsSuccess)
        {
            var raw = TagValueConverter.ToRaw(s.Tag, value);
            OnValue(s.Tag, new TagValue(TagValueConverter.ToEngineering(s.Tag, raw), TagQuality.Good, DateTime.Now));
        }
        else
        {
            logger.LogWarning("写点位 {Tag}={Value} 失败：{Message}", fullName, value, r.Message);
        }
        return r;
    }

    public IReadOnlyList<DeviceStatusInfo> GetStatuses() => _channels.Select(c => c.Status).ToList();

    public async Task<DeviceResult> TestConnectionAsync(string driverKey, IReadOnlyDictionary<string, string> options, CancellationToken ct = default)
    {
        try
        {
            await using var driver = registry.Create(driverKey, options);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            return await driver.ConnectAsync(cts.Token);
        }
        catch (Exception ex)
        {
            return DeviceResult.Fail(ex.Message);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
