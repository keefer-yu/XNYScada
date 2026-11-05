using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Devices;
using XNY.Scada.Device;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>诊断页面的视图模型。</summary>
public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    /// <summary>设备。</summary>
    private readonly IDeviceManager _devices;
    /// <summary>点位。</summary>
    private readonly ITagService _tags;
    /// <summary>配置。</summary>
    private readonly DeviceConfigService _config;

    /// <summary>创建诊断视图模型。</summary>
    public DiagnosticsViewModel(ViewModelContext context, IDeviceManager devices, ITagService tags, DeviceConfigService config, IDriverRegistry drivers) : base(context)
    {
        _devices = devices;
        _tags = tags;
        _config = config;
        Drivers = drivers.Drivers;
        WriteCommand = Secured(Permissions.Device.ManualWrite, WriteAsync, () => SelectedTag != null);
        ReadCommand = Command(ReadAsync, () => SelectedTag != null);
        ReloadCommand = Secured(Permissions.Device.Reload, async () =>
        {
            await _config.ApplyAsync();
            Tags.Clear();
        });
    }

    /// <summary>标题。</summary>
    public override string Title => "通讯诊断";

    /// <summary>驱动。</summary>
    public IReadOnlyList<DriverDescriptor> Drivers { get; }

    /// <summary>设备数量。</summary>
    public ObservableCollection<DeviceStatusItem> Devices { get; } = [];

    /// <summary>点位数量。</summary>
    public ObservableCollection<TagValueItem> Tags { get; } = [];

    /// <summary>已筛选点位。</summary>
    public ObservableCollection<TagValueItem> FilteredTags { get; } = [];

    /// <summary>筛选。</summary>
    [ObservableProperty] private string? _filter;

    /// <summary>选中标记。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WriteCommand), nameof(ReadCommand))]
    private TagValueItem? _selectedTag;

    /// <summary>写值。</summary>
    [ObservableProperty] private string _writeValue = "";

    /// <summary>写命令。</summary>
    public IAsyncRelayCommand WriteCommand { get; }
    /// <summary>只读命令。</summary>
    public IAsyncRelayCommand ReadCommand { get; }
    /// <summary>重载命令。</summary>
    public IAsyncRelayCommand ReloadCommand { get; }

    /// <summary>处理激活。</summary>
    protected override Task OnActivatedAsync()
    {
        Refresh();
        StartRefresh(TimeSpan.FromMilliseconds(500), () =>
        {
            Refresh();
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }

    /// <summary>当筛选变化时更新关联状态。</summary>
    partial void OnFilterChanged(string? value) => ApplyFilter();

    /// <summary>刷新。</summary>
    private void Refresh()
    {
        var statuses = _devices.GetStatuses();
        foreach (var s in statuses)
        {
            var item = Devices.FirstOrDefault(d => d.Name == s.DeviceName);
            if (item == null) Devices.Add(item = new DeviceStatusItem { Name = s.DeviceName });
            item.Update(s);
        }
        foreach (var gone in Devices.Where(d => statuses.All(s => s.DeviceName != d.Name)).ToList()) Devices.Remove(gone);

        var all = _tags.GetAll();
        if (all.Count != Tags.Count)
        {
            Replace(Tags, all.Select(s => new TagValueItem { FullName = s.Tag.FullName, Address = s.Tag.Address, DataType = s.Tag.DataType.ToString(), Access = s.Tag.Access.ToString() }));
            ApplyFilter();
        }
        var map = all.ToDictionary(s => s.Tag.FullName);
        foreach (var t in Tags)
        {
            if (map.TryGetValue(t.FullName, out var s)) t.Update(s.Value);
        }
    }

    /// <summary>应用筛选。</summary>
    private void ApplyFilter()
    {
        var f = Filter?.Trim();
        Replace(FilteredTags, string.IsNullOrEmpty(f) ? Tags : Tags.Where(t => t.FullName.Contains(f, StringComparison.OrdinalIgnoreCase) || t.Address.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>读取。</summary>
    private async Task ReadAsync()
    {
        if (SelectedTag == null) return;
        var r = await _tags.ReadAsync(SelectedTag.FullName);
        StatusMessage = r.IsSuccess ? $"{SelectedTag.FullName} = {r.Value}" : $"读取失败：{r.Message}";
    }

    /// <summary>写入。</summary>
    private async Task WriteAsync()
    {
        if (SelectedTag == null) return;
        var signer = await SignAsync(Permissions.Device.ManualWrite, $"手动写入 {SelectedTag.FullName} = {WriteValue}");
        if (signer == null) return;
        var r = await _config.ManualWriteAsync(SelectedTag.FullName, WriteValue, signer);
        StatusMessage = r.IsSuccess ? $"写入成功：{SelectedTag.FullName} = {WriteValue}" : $"写入失败：{r.Message}";
        if (!r.IsSuccess) Dialogs.ShowError(r.Message);
    }
}
