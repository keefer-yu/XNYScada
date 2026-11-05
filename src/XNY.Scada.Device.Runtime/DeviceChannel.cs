using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Device.Runtime;

internal sealed class ScanGroup(string name, int intervalMs, List<TagDefinition> tags)
{
    public string Name { get; } = name;
    public int IntervalMs { get; } = Math.Max(10, intervalMs);
    public List<TagDefinition> Tags { get; } = tags;
    public long NextDueTicks { get; set; }
}

/// <summary>单台设备的通讯通道：独立轮询任务、断线指数退避重连、诊断统计</summary>
internal sealed class DeviceChannel(
    DeviceEntity device,
    IPlcDriver driver,
    List<ScanGroup> groups,
    Action<TagDefinition, TagValue> onValue,
    Action<DeviceChannel> onStatusChanged,
    ILogger logger)
{
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _failedConnects;

    public DeviceEntity Device { get; } = device;
    public IPlcDriver Driver { get; } = driver;
    public IReadOnlyList<ScanGroup> Groups => groups;
    public long ReadCount;
    public long ErrorCount;
    public double LastScanMs;
    public string? LastError;
    public DateTime? LastConnectedAt;
    public bool Connected { get; private set; }

    public DeviceStatusInfo Status => new(Device.Name, Device.DriverKey, Device.Enabled, Connected,
        Interlocked.Read(ref ReadCount), Interlocked.Read(ref ErrorCount), LastScanMs, LastError, LastConnectedAt);

    public void Start()
    {
        if (!Device.Enabled) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Factory.StartNew(() => RunAsync(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

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

    private void MarkAllBad()
    {
        foreach (var t in groups.SelectMany(g => g.Tags)) onValue(t, TagValue.Bad());
    }

    private void SetConnected(bool connected)
    {
        if (Connected == connected) return;
        Connected = connected;
        onStatusChanged(this);
    }
}
