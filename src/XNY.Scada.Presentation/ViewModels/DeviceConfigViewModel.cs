using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Devices;
using XNY.Scada.Device;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>驱动参数项，根据驱动的 [DriverOption] 声明动态生成</summary>
public sealed partial class DriverOptionItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public DriverOptionType Type { get; init; }
    public IReadOnlyList<string> Choices { get; init; } = [];
    public bool IsEnum => Type == DriverOptionType.Enum;
    public bool IsBool => Type == DriverOptionType.Bool;

    [ObservableProperty] private string _value = "";
}

public sealed partial class DeviceConfigViewModel : ViewModelBase
{
    private readonly DeviceConfigService _service;
    private readonly IDeviceManager _deviceManager;

    public DeviceConfigViewModel(ViewModelContext context, DeviceConfigService service, IDriverRegistry drivers, IDeviceManager deviceManager) : base(context)
    {
        _service = service;
        _deviceManager = deviceManager;
        Drivers = drivers.Drivers;

        NewDeviceCommand = Secured(Permissions.Device.Edit, NewDeviceAsync);
        SaveDeviceCommand = Secured(Permissions.Device.Edit, SaveDeviceAsync);
        DeleteDeviceCommand = Secured(Permissions.Device.Edit, DeleteDeviceAsync, () => SelectedDevice is { Id: > 0 });
        TestConnectionCommand = Command(TestConnectionAsync);
        AddTagCommand = Secured(Permissions.Device.Edit, AddTagAsync);
        RemoveTagCommand = Secured(Permissions.Device.Edit, RemoveTagAsync);
        SaveTagsCommand = Secured(Permissions.Device.Edit, SaveTagsAsync);
        AddGroupCommand = Secured(Permissions.Device.Edit, AddGroupAsync);
        SaveGroupsCommand = Secured(Permissions.Device.Edit, SaveGroupsAsync);
        ImportCsvCommand = Secured(Permissions.Device.Edit, ImportCsvAsync);
        ExportCsvCommand = Command(ExportCsvAsync);
        ApplyCommand = Secured(Permissions.Device.Reload, ApplyAsync);
        AddAlarmCommand = Secured(Permissions.Alarm.Configure, () => { AlarmDefinitions.Add(new AlarmDefinitionEntity { Message = "新报警" }); return Task.CompletedTask; });
        RemoveAlarmCommand = Secured(Permissions.Alarm.Configure, () => { if (SelectedAlarm != null) AlarmDefinitions.Remove(SelectedAlarm); return Task.CompletedTask; });
        SaveAlarmsCommand = Secured(Permissions.Alarm.Configure, () => _service.SaveAlarmDefinitionsAsync(AlarmDefinitions.ToList()));
    }

    public override string Title => "设备配置";

    public IReadOnlyList<DriverDescriptor> Drivers { get; }

    public IReadOnlyList<TagDataType> DataTypes { get; } = Enum.GetValues<TagDataType>();

    public IReadOnlyList<TagAccess> AccessModes { get; } = Enum.GetValues<TagAccess>();

    public IReadOnlyList<AlarmKind> AlarmKinds { get; } = Enum.GetValues<AlarmKind>();

    public IReadOnlyList<AlarmLevel> AlarmLevels { get; } = Enum.GetValues<AlarmLevel>();

    public ObservableCollection<DeviceEntity> Devices { get; } = [];
    public ObservableCollection<DriverOptionItem> DriverOptions { get; } = [];
    public ObservableCollection<TagGroupEntity> Groups { get; } = [];
    public ObservableCollection<TagEntity> Tags { get; } = [];
    public ObservableCollection<AlarmDefinitionEntity> AlarmDefinitions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteDeviceCommand))]
    private DeviceEntity? _selectedDevice;

    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _driverKey = "Simulator";
    [ObservableProperty] private bool _deviceEnabled = true;
    [ObservableProperty] private string? _deviceDescription;
    [ObservableProperty] private string _addressHelp = "";
    [ObservableProperty] private TagEntity? _selectedTag;
    [ObservableProperty] private AlarmDefinitionEntity? _selectedAlarm;
    [ObservableProperty] private bool _hasPendingChanges;

    public IAsyncRelayCommand NewDeviceCommand { get; }
    public IAsyncRelayCommand SaveDeviceCommand { get; }
    public IAsyncRelayCommand DeleteDeviceCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand AddTagCommand { get; }
    public IAsyncRelayCommand RemoveTagCommand { get; }
    public IAsyncRelayCommand SaveTagsCommand { get; }
    public IAsyncRelayCommand AddGroupCommand { get; }
    public IAsyncRelayCommand SaveGroupsCommand { get; }
    public IAsyncRelayCommand ImportCsvCommand { get; }
    public IAsyncRelayCommand ExportCsvCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand AddAlarmCommand { get; }
    public IAsyncRelayCommand RemoveAlarmCommand { get; }
    public IAsyncRelayCommand SaveAlarmsCommand { get; }

    protected override async Task OnActivatedAsync()
    {
        await LoadDevicesAsync(SelectedDevice?.Id);
        if (SelectedDevice == null) await LoadDeviceAsync(null);
        Replace(AlarmDefinitions, await _service.GetAlarmDefinitionsAsync());
    }

    private async Task LoadDevicesAsync(long? selectId)
    {
        Replace(Devices, await _service.GetDevicesAsync());
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selectId) ?? Devices.FirstOrDefault();
    }

    partial void OnSelectedDeviceChanged(DeviceEntity? value) => _ = RunAsync(() => LoadDeviceAsync(value));

    private async Task LoadDeviceAsync(DeviceEntity? d)
    {
        DeviceName = d?.Name ?? "";
        DeviceEnabled = d?.Enabled ?? true;
        DeviceDescription = d?.Description;
        var options = Json.Deserialize<Dictionary<string, string>>(d?.OptionsJson) ?? new();
        DriverKey = d?.DriverKey ?? "Simulator";
        BuildOptions(options);
        Replace(Groups, d == null || d.Id == 0 ? [] : await _service.GetGroupsAsync(d.Id));
        Replace(Tags, d == null || d.Id == 0 ? [] : await _service.GetTagsAsync(d.Id));
    }

    partial void OnDriverKeyChanged(string value) => BuildOptions(DriverOptions.ToDictionary(o => o.Key, o => o.Value));

    private void BuildOptions(IReadOnlyDictionary<string, string> values)
    {
        var d = Drivers.FirstOrDefault(x => x.Key == DriverKey);
        AddressHelp = d?.AddressHelp ?? "";
        Replace(DriverOptions, (d?.Options ?? []).Select(o => new DriverOptionItem
        {
            Key = o.Key, DisplayName = o.DisplayName, Type = o.Type, Choices = o.Choices,
            Value = values.TryGetValue(o.Key, out var v) ? v : o.Default
        }));
    }

    private Dictionary<string, string> CollectOptions() => DriverOptions.ToDictionary(o => o.Key, o => o.Value);

    private Task NewDeviceAsync()
    {
        SelectedDevice = new DeviceEntity { Name = $"PLC{Devices.Count + 1}", DriverKey = "Siemens.S7", OptionsJson = "{}" };
        return Task.CompletedTask;
    }

    private async Task SaveDeviceAsync()
    {
        var d = SelectedDevice ?? new DeviceEntity();
        d.Name = DeviceName.Trim();
        d.DriverKey = DriverKey;
        d.Enabled = DeviceEnabled;
        d.Description = DeviceDescription;
        d.OptionsJson = Json.Serialize(CollectOptions());
        await _service.SaveDeviceAsync(d);
        HasPendingChanges = true;
        await LoadDevicesAsync(d.Id);
        StatusMessage = "设备已保存，点击「应用配置」使通讯生效";
    }

    private async Task DeleteDeviceAsync()
    {
        if (SelectedDevice == null || !Dialogs.Confirm($"删除设备 {SelectedDevice.Name} 及其全部点位？")) return;
        await _service.DeleteDeviceAsync(SelectedDevice.Id);
        HasPendingChanges = true;
        await LoadDevicesAsync(null);
    }

    private async Task TestConnectionAsync()
    {
        var r = await _deviceManager.TestConnectionAsync(DriverKey, CollectOptions());
        if (r.IsSuccess) Dialogs.ShowInfo("连接成功");
        else Dialogs.ShowError($"连接失败：{r.Message}");
    }

    private Task AddTagAsync()
    {
        var tag = new TagEntity { Name = $"Tag{Tags.Count + 1}", Address = "", DataType = TagDataType.Int16, GroupId = Groups.FirstOrDefault()?.Id ?? 0 };
        Tags.Add(tag);
        SelectedTag = tag;
        return Task.CompletedTask;
    }

    private Task RemoveTagAsync()
    {
        if (SelectedTag != null) Tags.Remove(SelectedTag);
        return Task.CompletedTask;
    }

    private async Task SaveTagsAsync()
    {
        if (SelectedDevice is not { Id: > 0 } d) throw new InvalidOperationException("请先保存设备");
        await _service.SaveTagsAsync(d.Id, Tags.ToList());
        Replace(Tags, await _service.GetTagsAsync(d.Id));
        HasPendingChanges = true;
        StatusMessage = "点位已保存，点击「应用配置」使通讯生效";
    }

    private async Task AddGroupAsync()
    {
        if (SelectedDevice is not { Id: > 0 } d) throw new InvalidOperationException("请先保存设备");
        var name = Dialogs.Prompt("扫描组名称", "新增扫描组", $"Group{Groups.Count + 1}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var g = await _service.SaveGroupAsync(new TagGroupEntity { DeviceId = d.Id, Name = name, ScanIntervalMs = 100 });
        Groups.Add(g);
    }

    private async Task SaveGroupsAsync()
    {
        foreach (var g in Groups) await _service.SaveGroupAsync(g);
        HasPendingChanges = true;
        StatusMessage = "扫描组已保存";
    }

    private async Task ImportCsvAsync()
    {
        if (SelectedDevice is not { Id: > 0 } d) throw new InvalidOperationException("请先保存设备");
        var path = Dialogs.OpenFile("CSV 文件|*.csv");
        if (path == null) return;
        var n = await _service.ImportTagsCsvAsync(d.Id, path);
        Replace(Groups, await _service.GetGroupsAsync(d.Id));
        Replace(Tags, await _service.GetTagsAsync(d.Id));
        HasPendingChanges = true;
        Dialogs.ShowInfo($"已导入 {n} 个点位");
    }

    private async Task ExportCsvAsync()
    {
        if (SelectedDevice is not { Id: > 0 } d) return;
        var path = Dialogs.SaveFile("CSV 文件|*.csv", $"{d.Name}_tags.csv");
        if (path == null) return;
        await _service.ExportTagsCsvAsync(d.Id, path);
        StatusMessage = $"已导出到 {path}";
    }

    private async Task ApplyAsync()
    {
        await _service.ApplyAsync();
        HasPendingChanges = false;
        StatusMessage = "通讯已按新配置重新启动";
    }
}
