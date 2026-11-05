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
    /// <summary>驱动选项键。</summary>
    public string Key { get; init; } = "";
    /// <summary>显示名。</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>驱动选项类型。</summary>
    public DriverOptionType Type { get; init; }
    /// <summary>可选值。</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];
    /// <summary>是否枚举。</summary>
    public bool IsEnum => Type == DriverOptionType.Enum;
    /// <summary>是否布尔。</summary>
    public bool IsBool => Type == DriverOptionType.Bool;

    /// <summary>驱动选项值。</summary>
    [ObservableProperty] private string _value = "";
}

/// <summary>设备配置页面的视图模型。</summary>
public sealed partial class DeviceConfigViewModel : ViewModelBase
{
    /// <summary>服务。</summary>
    private readonly DeviceConfigService _service;
    /// <summary>设备管理器。</summary>
    private readonly IDeviceManager _deviceManager;

    /// <summary>创建设备配置视图模型。</summary>
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

    /// <summary>标题。</summary>
    public override string Title => "设备配置";

    /// <summary>驱动。</summary>
    public IReadOnlyList<DriverDescriptor> Drivers { get; }

    /// <summary>数据类型。</summary>
    public IReadOnlyList<TagDataType> DataTypes { get; } = Enum.GetValues<TagDataType>();

    /// <summary>访问模式。</summary>
    public IReadOnlyList<TagAccess> AccessModes { get; } = Enum.GetValues<TagAccess>();

    /// <summary>报警种类。</summary>
    public IReadOnlyList<AlarmKind> AlarmKinds { get; } = Enum.GetValues<AlarmKind>();

    /// <summary>报警级别。</summary>
    public IReadOnlyList<AlarmLevel> AlarmLevels { get; } = Enum.GetValues<AlarmLevel>();

    /// <summary>设备数量。</summary>
    public ObservableCollection<DeviceEntity> Devices { get; } = [];
    /// <summary>驱动选项。</summary>
    public ObservableCollection<DriverOptionItem> DriverOptions { get; } = [];
    /// <summary>组。</summary>
    public ObservableCollection<TagGroupEntity> Groups { get; } = [];
    /// <summary>点位数量。</summary>
    public ObservableCollection<TagEntity> Tags { get; } = [];
    /// <summary>报警定义。</summary>
    public ObservableCollection<AlarmDefinitionEntity> AlarmDefinitions { get; } = [];

    /// <summary>选中设备。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteDeviceCommand))]
    private DeviceEntity? _selectedDevice;

    /// <summary>设备名称。</summary>
    [ObservableProperty] private string _deviceName = "";
    /// <summary>驱动键。</summary>
    [ObservableProperty] private string _driverKey = "Simulator";
    /// <summary>设备启用。</summary>
    [ObservableProperty] private bool _deviceEnabled = true;
    /// <summary>设备描述。</summary>
    [ObservableProperty] private string? _deviceDescription;
    /// <summary>地址帮助。</summary>
    [ObservableProperty] private string _addressHelp = "";
    /// <summary>选中标记。</summary>
    [ObservableProperty] private TagEntity? _selectedTag;
    /// <summary>选中报警。</summary>
    [ObservableProperty] private AlarmDefinitionEntity? _selectedAlarm;
    /// <summary>是否有待处理变更。</summary>
    [ObservableProperty] private bool _hasPendingChanges;

    /// <summary>新设备命令。</summary>
    public IAsyncRelayCommand NewDeviceCommand { get; }
    /// <summary>保存设备命令。</summary>
    public IAsyncRelayCommand SaveDeviceCommand { get; }
    /// <summary>删除设备命令。</summary>
    public IAsyncRelayCommand DeleteDeviceCommand { get; }
    /// <summary>测试连接命令。</summary>
    public IAsyncRelayCommand TestConnectionCommand { get; }
    /// <summary>添加标记命令。</summary>
    public IAsyncRelayCommand AddTagCommand { get; }
    /// <summary>移除标记命令。</summary>
    public IAsyncRelayCommand RemoveTagCommand { get; }
    /// <summary>保存点位命令。</summary>
    public IAsyncRelayCommand SaveTagsCommand { get; }
    /// <summary>添加组命令。</summary>
    public IAsyncRelayCommand AddGroupCommand { get; }
    /// <summary>保存组命令。</summary>
    public IAsyncRelayCommand SaveGroupsCommand { get; }
    /// <summary>导入CSV命令。</summary>
    public IAsyncRelayCommand ImportCsvCommand { get; }
    /// <summary>导出CSV命令。</summary>
    public IAsyncRelayCommand ExportCsvCommand { get; }
    /// <summary>应用命令。</summary>
    public IAsyncRelayCommand ApplyCommand { get; }
    /// <summary>添加报警命令。</summary>
    public IAsyncRelayCommand AddAlarmCommand { get; }
    /// <summary>移除报警命令。</summary>
    public IAsyncRelayCommand RemoveAlarmCommand { get; }
    /// <summary>保存报警命令。</summary>
    public IAsyncRelayCommand SaveAlarmsCommand { get; }

    /// <summary>处理激活。</summary>
    protected override async Task OnActivatedAsync()
    {
        await LoadDevicesAsync(SelectedDevice?.Id);
        if (SelectedDevice == null) await LoadDeviceAsync(null);
        Replace(AlarmDefinitions, await _service.GetAlarmDefinitionsAsync());
    }

    /// <summary>加载设备。</summary>
    private async Task LoadDevicesAsync(long? selectId)
    {
        Replace(Devices, await _service.GetDevicesAsync());
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selectId) ?? Devices.FirstOrDefault();
    }

    /// <summary>当选中设备变化时更新关联状态。</summary>
    partial void OnSelectedDeviceChanged(DeviceEntity? value) => _ = RunAsync(() => LoadDeviceAsync(value));

    /// <summary>加载设备。</summary>
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

    /// <summary>当驱动键变化时更新关联状态。</summary>
    partial void OnDriverKeyChanged(string value) => BuildOptions(DriverOptions.ToDictionary(o => o.Key, o => o.Value));

    /// <summary>构建选项。</summary>
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

    /// <summary>收集选项。</summary>
    private Dictionary<string, string> CollectOptions() => DriverOptions.ToDictionary(o => o.Key, o => o.Value);

    /// <summary>新建设备。</summary>
    private Task NewDeviceAsync()
    {
        SelectedDevice = new DeviceEntity { Name = $"PLC{Devices.Count + 1}", DriverKey = "Siemens.S7", OptionsJson = "{}" };
        return Task.CompletedTask;
    }

    /// <summary>保存设备。</summary>
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

    /// <summary>删除设备。</summary>
    private async Task DeleteDeviceAsync()
    {
        if (SelectedDevice == null || !Dialogs.Confirm($"删除设备 {SelectedDevice.Name} 及其全部点位？")) return;
        await _service.DeleteDeviceAsync(SelectedDevice.Id);
        HasPendingChanges = true;
        await LoadDevicesAsync(null);
    }

    /// <summary>测试连接。</summary>
    private async Task TestConnectionAsync()
    {
        var r = await _deviceManager.TestConnectionAsync(DriverKey, CollectOptions());
        if (r.IsSuccess) Dialogs.ShowInfo("连接成功");
        else Dialogs.ShowError($"连接失败：{r.Message}");
    }

    /// <summary>添加标记。</summary>
    private Task AddTagAsync()
    {
        var tag = new TagEntity { Name = $"Tag{Tags.Count + 1}", Address = "", DataType = TagDataType.Int16, GroupId = Groups.FirstOrDefault()?.Id ?? 0 };
        Tags.Add(tag);
        SelectedTag = tag;
        return Task.CompletedTask;
    }

    /// <summary>移除标记。</summary>
    private Task RemoveTagAsync()
    {
        if (SelectedTag != null) Tags.Remove(SelectedTag);
        return Task.CompletedTask;
    }

    /// <summary>保存点位。</summary>
    private async Task SaveTagsAsync()
    {
        if (SelectedDevice is not { Id: > 0 } d) throw new InvalidOperationException("请先保存设备");
        await _service.SaveTagsAsync(d.Id, Tags.ToList());
        Replace(Tags, await _service.GetTagsAsync(d.Id));
        HasPendingChanges = true;
        StatusMessage = "点位已保存，点击「应用配置」使通讯生效";
    }

    /// <summary>添加组。</summary>
    private async Task AddGroupAsync()
    {
        if (SelectedDevice is not { Id: > 0 } d) throw new InvalidOperationException("请先保存设备");
        var name = Dialogs.Prompt("扫描组名称", "新增扫描组", $"Group{Groups.Count + 1}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var g = await _service.SaveGroupAsync(new TagGroupEntity { DeviceId = d.Id, Name = name, ScanIntervalMs = 100 });
        Groups.Add(g);
    }

    /// <summary>保存组。</summary>
    private async Task SaveGroupsAsync()
    {
        foreach (var g in Groups) await _service.SaveGroupAsync(g);
        HasPendingChanges = true;
        StatusMessage = "扫描组已保存";
    }

    /// <summary>导入CSV。</summary>
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

    /// <summary>导出CSV。</summary>
    private async Task ExportCsvAsync()
    {
        if (SelectedDevice is not { Id: > 0 } d) return;
        var path = Dialogs.SaveFile("CSV 文件|*.csv", $"{d.Name}_tags.csv");
        if (path == null) return;
        await _service.ExportTagsCsvAsync(d.Id, path);
        StatusMessage = $"已导出到 {path}";
    }

    /// <summary>应用。</summary>
    private async Task ApplyAsync()
    {
        await _service.ApplyAsync();
        HasPendingChanges = false;
        StatusMessage = "通讯已按新配置重新启动";
    }
}
