using CommunityToolkit.Mvvm.ComponentModel;
using XNY.Scada.Device;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>设备状态项。</summary>
public sealed partial class DeviceStatusItem : ObservableObject
{
    /// <summary>设备状态名称。</summary>
    public string Name { get; init; } = "";

    /// <summary>驱动键。</summary>
    [ObservableProperty] private string _driverKey = "";
    /// <summary>连接。</summary>
    [ObservableProperty] private bool _connected;
    /// <summary>启用。</summary>
    [ObservableProperty] private bool _enabled;
    /// <summary>读取次数。</summary>
    [ObservableProperty] private long _readCount;
    /// <summary>错误数量。</summary>
    [ObservableProperty] private long _errorCount;
    /// <summary>最近扫描毫秒。</summary>
    [ObservableProperty] private double _lastScanMs;
    /// <summary>最近错误。</summary>
    [ObservableProperty] private string? _lastError;
    /// <summary>最近连接时间。</summary>
    [ObservableProperty] private DateTime? _lastConnectedAt;

    /// <summary>状态文本。</summary>
    public string StatusText => !Enabled ? "停用" : Connected ? "在线" : "离线";

    /// <summary>更新。</summary>
    public void Update(DeviceStatusInfo s)
    {
        DriverKey = s.DriverKey;
        Connected = s.Connected;
        Enabled = s.Enabled;
        ReadCount = s.ReadCount;
        ErrorCount = s.ErrorCount;
        LastScanMs = Math.Round(s.LastScanMs, 1);
        LastError = s.LastError;
        LastConnectedAt = s.LastConnectedAt;
        OnPropertyChanged(nameof(StatusText));
    }
}

/// <summary>标记值项。</summary>
public sealed partial class TagValueItem : ObservableObject
{
    /// <summary>点位全名。</summary>
    public string FullName { get; init; } = "";
    /// <summary>PLC 地址。</summary>
    public string Address { get; init; } = "";
    /// <summary>数据类型。</summary>
    public string DataType { get; init; } = "";
    /// <summary>访问方向。</summary>
    public string Access { get; init; } = "";

    /// <summary>标记值值。</summary>
    [ObservableProperty] private string _value = "";
    /// <summary>质量。</summary>
    [ObservableProperty] private string _quality = "";
    /// <summary>时间戳。</summary>
    [ObservableProperty] private DateTime _timestamp;

    /// <summary>更新。</summary>
    public void Update(TagValue v)
    {
        Value = v.Value switch
        {
            null => "",
            double d => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            _ => Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
        };
        Quality = v.Quality.ToString();
        Timestamp = v.Timestamp;
    }
}

/// <summary>托盘单元格项。</summary>
public sealed partial class TrayCellItem : ObservableObject
{
    /// <summary>位置。</summary>
    public int Position { get; init; }

    /// <summary>单元格代码。</summary>
    [ObservableProperty] private string? _cellCode;
    /// <summary>是否有单元格。</summary>
    [ObservableProperty] private bool _hasCell;
}

/// <summary>档位项。</summary>
public sealed partial class BinItem : ObservableObject
{
    /// <summary>档位号。</summary>
    public int BinNo { get; init; }

    /// <summary>档位名称。</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>档位数量。</summary>
    [ObservableProperty] private int _count;
    /// <summary>容量。</summary>
    [ObservableProperty] private int _capacity;
    /// <summary>是否整行。</summary>
    [ObservableProperty] private bool _isFull;
    /// <summary>是否NG。</summary>
    [ObservableProperty] private bool _isNg;

    /// <summary>占用百分比。</summary>
    public double Percent => Capacity <= 0 ? 0 : Math.Min(100, Count * 100.0 / Capacity);

    /// <summary>当数量变化时更新关联状态。</summary>
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Percent));
}

/// <summary>NG数量项。</summary>
/// <param name="Code">编码。</param>
/// <param name="Count">数量。</param>
public sealed record NgCountItem(string Code, int Count);
