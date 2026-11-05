using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Device.Runtime;

/// <summary>设备管理器：加载设备配置、启动通讯通道、维护点位缓存，同时实现 <see cref="ITagService"/></summary>
/// <param name="db">数据库客户端。</param>
/// <param name="registry">注册。</param>
/// <param name="logger">日志记录器。</param>
public sealed class DeviceManager(ISqlSugarClient db, IDriverRegistry registry, ILogger<DeviceManager> logger)
    : IDeviceManager, ITagService, IAsyncDisposable
{
    /// <summary>标记状态。</summary>
    /// <param name="tag">标记。</param>
    /// <param name="channel">通道。</param>
    private sealed class TagState(TagDefinition tag, DeviceChannel channel)
    {
        /// <summary>标记。</summary>
        public TagDefinition Tag { get; } = tag;
        /// <summary>通道。</summary>
        public DeviceChannel Channel { get; } = channel;
        /// <summary>标记状态值。</summary>
        public TagValue? Value;
    }

    /// <summary>同步。</summary>
    private readonly SemaphoreSlim _sync = new(1, 1);
    /// <summary>点位。</summary>
    private ConcurrentDictionary<string, TagState> _tags = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>通道。</summary>
    private List<DeviceChannel> _channels = [];

    /// <summary>标记发生变化时触发。</summary>
    public event EventHandler<TagChangedEventArgs>? TagChanged;

    /// <summary>状态发生变化时触发。</summary>
    public event EventHandler<DeviceStatusInfo>? StatusChanged;

    /// <summary>是否正在运行。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>启动。</summary>
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

    /// <summary>停止。</summary>
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

    /// <summary>重新加载。</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await StopAsync();
        await StartAsync(ct);
    }

    /// <summary>加载。</summary>
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

    /// <summary>转换为定义。</summary>
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

    /// <summary>处理值。</summary>
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

    /// <summary>查找标记。</summary>
    public TagDefinition? FindTag(string fullName) => _tags.TryGetValue(fullName, out var s) ? s.Tag : null;

    /// <summary>获取值。</summary>
    public TagValue? GetValue(string fullName) => _tags.TryGetValue(fullName, out var s) ? s.Value : null;

    /// <summary>获取全部。</summary>
    public IReadOnlyList<TagSnapshot> GetAll() =>
        _tags.Values.Select(s => new TagSnapshot(s.Tag, s.Value ?? TagValue.Bad())).OrderBy(s => s.Tag.FullName).ToList();

    /// <summary>读取。</summary>
    public async Task<DeviceResult<object?>> ReadAsync(string fullName, CancellationToken ct = default)
    {
        if (!_tags.TryGetValue(fullName, out var s)) return DeviceResult<object?>.Fail($"点位不存在: {fullName}");
        if (!s.Channel.Driver.IsConnected) return DeviceResult<object?>.Fail($"设备 {s.Tag.DeviceName} 未连接");
        var r = await s.Channel.Driver.ReadAsync(s.Tag, ct);
        if (r.IsSuccess) OnValue(s.Tag, new TagValue(r.Value, TagQuality.Good, DateTime.Now));
        return r;
    }

    /// <summary>写入。</summary>
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

    /// <summary>获取状态。</summary>
    public IReadOnlyList<DeviceStatusInfo> GetStatuses() => _channels.Select(c => c.Status).ToList();

    /// <summary>测试连接。</summary>
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

    /// <summary>释放资源。</summary>
    public async ValueTask DisposeAsync() => await StopAsync();
}
