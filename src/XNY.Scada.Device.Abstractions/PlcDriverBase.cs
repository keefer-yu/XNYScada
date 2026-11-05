using System.Globalization;

namespace XNY.Scada.Device;

/// <summary>驱动基类：提供参数读取、读写串行化与默认的逐个批量读取</summary>
public abstract class PlcDriverBase : IPlcDriver
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    protected IReadOnlyDictionary<string, string> Options { get; private set; } = new Dictionary<string, string>();

    public abstract string DriverKey { get; }

    public abstract bool IsConnected { get; }

    public virtual void Configure(IReadOnlyDictionary<string, string> options) => Options = options;

    protected string GetOption(string key, string defaultValue = "") =>
        Options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : defaultValue;

    protected int GetInt(string key, int defaultValue) =>
        int.TryParse(GetOption(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;

    protected bool GetBool(string key, bool defaultValue) =>
        bool.TryParse(GetOption(key), out var v) ? v : defaultValue;

    public abstract Task<DeviceResult> ConnectAsync(CancellationToken ct);

    public abstract Task DisconnectAsync();

    protected abstract DeviceResult<object?> ReadCore(TagDefinition tag);

    protected abstract DeviceResult WriteCore(TagDefinition tag, object raw);

    public async Task<DeviceResult<object?>> ReadAsync(TagDefinition tag, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var r = await Task.Run(() => ReadCore(tag), ct).ConfigureAwait(false);
            return r.IsSuccess ? DeviceResult<object?>.Ok(TagValueConverter.ToEngineering(tag, r.Value)) : r;
        }
        catch (Exception ex)
        {
            return DeviceResult<object?>.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public virtual async Task<DeviceResult<object?[]>> ReadBatchAsync(IReadOnlyList<TagDefinition> tags, CancellationToken ct)
    {
        var values = new object?[tags.Count];
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                for (var i = 0; i < tags.Count; i++)
                {
                    var r = ReadCore(tags[i]);
                    if (!r.IsSuccess) return DeviceResult<object?[]>.Fail($"{tags[i].FullName}: {r.Message}");
                    values[i] = TagValueConverter.ToEngineering(tags[i], r.Value);
                }
                return DeviceResult<object?[]>.Ok(values);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return DeviceResult<object?[]>.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DeviceResult> WriteAsync(TagDefinition tag, object? value, CancellationToken ct)
    {
        if (tag.Access == Domain.TagAccess.Read) return DeviceResult.Fail($"点位 {tag.FullName} 只读");
        object raw;
        try
        {
            raw = TagValueConverter.ToRaw(tag, value);
        }
        catch (Exception ex)
        {
            return DeviceResult.Fail($"值转换失败: {ex.Message}");
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => WriteCore(tag, raw), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return DeviceResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
