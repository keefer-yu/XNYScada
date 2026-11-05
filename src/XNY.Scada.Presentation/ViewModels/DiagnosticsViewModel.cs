using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Devices;
using XNY.Scada.Device;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly IDeviceManager _devices;
    private readonly ITagService _tags;
    private readonly DeviceConfigService _config;

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

    public override string Title => "通讯诊断";

    public IReadOnlyList<DriverDescriptor> Drivers { get; }

    public ObservableCollection<DeviceStatusItem> Devices { get; } = [];

    public ObservableCollection<TagValueItem> Tags { get; } = [];

    public ObservableCollection<TagValueItem> FilteredTags { get; } = [];

    [ObservableProperty] private string? _filter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WriteCommand), nameof(ReadCommand))]
    private TagValueItem? _selectedTag;

    [ObservableProperty] private string _writeValue = "";

    public IAsyncRelayCommand WriteCommand { get; }
    public IAsyncRelayCommand ReadCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }

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

    partial void OnFilterChanged(string? value) => ApplyFilter();

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

    private void ApplyFilter()
    {
        var f = Filter?.Trim();
        Replace(FilteredTags, string.IsNullOrEmpty(f) ? Tags : Tags.Where(t => t.FullName.Contains(f, StringComparison.OrdinalIgnoreCase) || t.Address.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task ReadAsync()
    {
        if (SelectedTag == null) return;
        var r = await _tags.ReadAsync(SelectedTag.FullName);
        StatusMessage = r.IsSuccess ? $"{SelectedTag.FullName} = {r.Value}" : $"读取失败：{r.Message}";
    }

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
