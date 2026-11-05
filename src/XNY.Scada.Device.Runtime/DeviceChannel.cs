using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Device.Runtime;

/// <summary>扫描组。</summary>
/// <param name="name">名称。</param>
/// <param name="intervalMs">间隔毫秒。</param>
/// <param name="tags">点位。</param>
internal sealed class ScanGroup(string name, int intervalMs, List<TagDefinition> tags)
{
    /// <summary>扫描组名称。</summary>
    public string Name { get; } = name;
    /// <summary>定时间隔（毫秒）。</summary>
    public int IntervalMs { get; } = Math.Max(10, intervalMs);
    /// <summary>点位数量。</summary>
    public List<TagDefinition> Tags { get; } = tags;
    /// <summary>下一步到期节拍。</summary>
    public long NextDueTicks { get; set; }
}

/// <summary>单台设备的通讯通道：独立轮询任务、断线指数退避重连、诊断统计</summary>
/// <param name="device">设备。</param>
/// <param name="driver">驱动。</param>
/// <param name="groups">组。</param>
/// <param name="onValue">于值。</param>
/// <param name="onStatusChanged">于状态已变化。</param>
/// <param name="logger">日志记录器。</param>
internal sealed class DeviceChannel(
    DeviceEntity device,
    IPlcDriver driver,
    List<ScanGroup> groups,
    Action<TagDefinition, TagValue> onValue,
    Action<DeviceChannel> onStatusChanged,
    ILogger logger)
{
    /// <summary>取消源。</summary>
    private CancellationTokenSource? _cts;
    /// <summary>循环。</summary>
    private Task? _loop;
    /// <summary>失败连接。</summary>
    private int _failedConnects;

    /// <summary>设备。</summary>
    public DeviceEntity Device { get; } = device;
    /// <summary>驱动。</summary>
    public IPlcDriver Driver { get; } = driver;
    /// <summary>组。</summary>
    public IReadOnlyList<ScanGroup> Groups => groups;
    /// <summary>读取次数。</summary>
    public long ReadCount;
    /// <summary>错误次数。</summary>
    public long ErrorCount;
    /// <summary>最近一次扫描耗时（毫秒）。</summary>
    public double LastScanMs;
    /// <summary>最近一次错误。</summary>
    public string? LastError;
    /// <summary>最近一次连接成功的时间。</summary>
    public DateTime? LastConnectedAt;
    /// <summary>是否已连接。</summary>
    public bool Connected { get; private set; }

    /// <summary>设备通道状态。</summary>
    public DeviceStatusInfo Status => new(Device.Name, Device.DriverKey, Device.Enabled, Connected,
        Interlocked.Read(ref ReadCount), Interlocked.Read(ref ErrorCount), LastScanMs, LastError, LastConnectedAt);

    /// <summary>启动。</summary>
    public void Start()
    {
        if (!Device.Enabled) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Factory.StartNew(() => RunAsync(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    /// <summary>停止。</summary>
    public async Task StopAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        try
        {
            if (_loop != null) await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        await Driver.DisposeAsync().ConfigureAwait(false);
        SetConnected(false);
    }

    /// <summary>运行。</summary>
    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!Driver.IsConnected)
            {
                if (Connected) MarkAllBad();
                SetConnected(false);
                var r = await Driver.ConnectAsync(ct).ConfigureAwait(false);
                if (!r.IsSuccess)
                {
                    _failedConnects++;
                    LastError = r.Message;
                    Interlocked.Increment(ref ErrorCount);
                    var backoff = Math.Min(500 * (1 << Math.Min(_failedConnects, 5)), 10_000);
                    logger.LogWarning("设备 {Device} 连接失败({Count})：{Message}，{Backoff}ms 后重试", Device.Name, _failedConnects, r.Message, backoff);
                    onStatusChanged(this);
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    continue;
                }
                _failedConnects = 0;
                LastError = null;
                LastConnectedAt = DateTime.Now;
                SetConnected(true);
                logger.LogInformation("设备 {Device} 已连接", Device.Name);
            }

            var now = Stopwatch.GetTimestamp();
            foreach (var g in groups.Where(g => g.NextDueTicks <= now && g.Tags.Count > 0))
            {
                var sw = Stopwatch.StartNew();
                var r = await Driver.ReadBatchAsync(g.Tags, ct).ConfigureAwait(false);
                LastScanMs = sw.Elapsed.TotalMilliseconds;
                g.NextDueTicks = Stopwatch.GetTimestamp() + g.IntervalMs * Stopwatch.Frequency / 1000;
                if (r.IsSuccess)
                {
                    Interlocked.Increment(ref ReadCount);
                    var ts = DateTime.Now;
                    for (var i = 0; i < g.Tags.Count; i++) onValue(g.Tags[i], new TagValue(r.Value![i], TagQuality.Good, ts));
                }
                else
                {
                    Interlocked.Increment(ref ErrorCount);
                    LastError = r.Message;
                    onStatusChanged(this);
                    if (!Driver.IsConnected) break;
                }
            }

            var next = groups.Count == 0 ? Stopwatch.GetTimestamp() + Stopwatch.Frequency : groups.Min(g => g.NextDueTicks);
            var waitMs = (int)Math.Clamp((next - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency, 5, 1000);
            await Task.Delay(waitMs, ct).ConfigureAwait(false);
        }
    }

    /// <summary>标记全部不良。</summary>
    private void MarkAllBad()
    {
        foreach (var t in groups.SelectMany(g => g.Tags)) onValue(t, TagValue.Bad());
    }

    /// <summary>设置连接。</summary>
    private void SetConnected(bool connected)
    {
        if (Connected == connected) return;
        Connected = connected;
        onStatusChanged(this);
    }
}
