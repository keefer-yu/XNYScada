using CommunityToolkit.Mvvm.ComponentModel;
using XNY.Scada.Device;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class DeviceStatusItem : ObservableObject
{
    public string Name { get; init; } = "";

    [ObservableProperty] private string _driverKey = "";
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private long _readCount;
    [ObservableProperty] private long _errorCount;
    [ObservableProperty] private double _lastScanMs;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private DateTime? _lastConnectedAt;

    public string StatusText => !Enabled ? "停用" : Connected ? "在线" : "离线";

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

public sealed partial class TagValueItem : ObservableObject
{
    public string FullName { get; init; } = "";
    public string Address { get; init; } = "";
    public string DataType { get; init; } = "";
    public string Access { get; init; } = "";

    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _quality = "";
    [ObservableProperty] private DateTime _timestamp;

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

public sealed partial class TrayCellItem : ObservableObject
{
    public int Position { get; init; }

    [ObservableProperty] private string? _cellCode;
    [ObservableProperty] private bool _hasCell;
}

public sealed partial class BinItem : ObservableObject
{
    public int BinNo { get; init; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _count;
    [ObservableProperty] private int _capacity;
    [ObservableProperty] private bool _isFull;
    [ObservableProperty] private bool _isNg;

    public double Percent => Capacity <= 0 ? 0 : Math.Min(100, Count * 100.0 / Capacity);

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Percent));
}

public sealed record NgCountItem(string Code, int Count);
