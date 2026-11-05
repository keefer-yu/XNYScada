using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Settings;
using XNY.Scada.Application.Templates;
using XNY.Scada.Device;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Nodes;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class SystemViewModel : ViewModelBase
{
    private readonly SystemParamService _params;
    private readonly MachineTemplateService _templates;

    public SystemViewModel(ViewModelContext context, SystemParamService parameters, MachineTemplateService templates,
        IDriverRegistry drivers, INodeRegistry nodes) : base(context)
    {
        _params = parameters;
        _templates = templates;
        Drivers = drivers.Drivers;
        NodeTypes = nodes.Descriptors;
        MachineTypeChoices = MachineTypes.DisplayNames.ToList();

        SaveParamCommand = Secured(Permissions.System.Edit, SaveParamAsync, () => SelectedParam != null);
        AddParamCommand = Secured(Permissions.System.Edit, () =>
        {
            var p = new SystemParamEntity { Key = "NewParam", Group = "Custom" };
            Params.Add(p);
            SelectedParam = p;
            return Task.CompletedTask;
        });
        DeleteParamCommand = Secured(Permissions.System.Edit, DeleteParamAsync, () => SelectedParam is { Id: > 0 });
        SaveLayoutCommand = Secured(Permissions.System.Edit, SaveLayoutAsync);
        ImportTemplateCommand = Secured<TemplateInfo>(Permissions.System.ImportTemplate, ImportTemplateAsync);
        ImportTemplateFileCommand = Secured(Permissions.System.ImportTemplate, ImportTemplateFileAsync);
        ExportTemplateCommand = Secured(Permissions.System.Edit, ExportTemplateAsync);
    }

    public override string Title => "系统设置";

    public IReadOnlyList<DriverDescriptor> Drivers { get; }
    public IReadOnlyList<NodeDescriptor> NodeTypes { get; }
    public IReadOnlyList<KeyValuePair<string, string>> MachineTypeChoices { get; }

    public ObservableCollection<SystemParamEntity> Params { get; } = [];
    public ObservableCollection<TemplateInfo> Templates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveParamCommand), nameof(DeleteParamCommand))]
    private SystemParamEntity? _selectedParam;

    [ObservableProperty] private string _machineType = MachineTypes.Grading;
    [ObservableProperty] private string _machineName = "";
    [ObservableProperty] private string _station = "";
    [ObservableProperty] private bool _showTray;
    [ObservableProperty] private int _trayRows;
    [ObservableProperty] private int _trayCols;
    [ObservableProperty] private bool _showBins;
    [ObservableProperty] private int _binCount;
    [ObservableProperty] private bool _showNg;
    [ObservableProperty] private int _ngBoxCount;
    [ObservableProperty] private string _keyTags = "";
    [ObservableProperty] private string _manualFlows = "";
    [ObservableProperty] private bool _replaceDevices = true;

    public IAsyncRelayCommand SaveParamCommand { get; }
    public IAsyncRelayCommand AddParamCommand { get; }
    public IAsyncRelayCommand DeleteParamCommand { get; }
    public IAsyncRelayCommand SaveLayoutCommand { get; }
    public IAsyncRelayCommand<TemplateInfo> ImportTemplateCommand { get; }
    public IAsyncRelayCommand ImportTemplateFileCommand { get; }
    public IAsyncRelayCommand ExportTemplateCommand { get; }

    protected override async Task OnActivatedAsync()
    {
        await ReloadAsync();
        Replace(Templates, _templates.List());
    }

    private async Task ReloadAsync()
    {
        var key = SelectedParam?.Key;
        Replace(Params, await _params.ListAsync());
        SelectedParam = Params.FirstOrDefault(p => p.Key == key);
        MachineType = _params.MachineType;
        MachineName = _params.Get("MachineName");
        Station = _params.Station;
        var l = _params.Layout;
        ShowTray = l.ShowTray;
        TrayRows = l.TrayRows;
        TrayCols = l.TrayCols;
        ShowBins = l.ShowBins;
        BinCount = l.BinCount;
        ShowNg = l.ShowNg;
        NgBoxCount = l.NgBoxCount;
        KeyTags = string.Join(Environment.NewLine, l.KeyTags);
        ManualFlows = string.Join(Environment.NewLine, l.ManualFlows);
    }

    private async Task SaveParamAsync()
    {
        if (SelectedParam == null) return;
        await _params.SetAsync(SelectedParam.Key.Trim(), SelectedParam.Value, SelectedParam.Group, SelectedParam.Description);
        await ReloadAsync();
        StatusMessage = "参数已保存";
    }

    private async Task DeleteParamAsync()
    {
        if (SelectedParam == null || !Dialogs.Confirm($"删除参数 {SelectedParam.Key}？")) return;
        await _params.DeleteAsync(SelectedParam.Key);
        SelectedParam = null;
        await ReloadAsync();
    }

    private static List<string> Lines(string text) =>
        text.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async Task SaveLayoutAsync()
    {
        var layout = new MachineLayout
        {
            ShowTray = ShowTray, TrayRows = Math.Max(1, TrayRows), TrayCols = Math.Max(1, TrayCols),
            ShowBins = ShowBins, BinCount = Math.Max(1, BinCount), ShowNg = ShowNg, NgBoxCount = Math.Max(1, NgBoxCount),
            KeyTags = Lines(KeyTags), ManualFlows = Lines(ManualFlows)
        };
        await _params.SetAsync("MachineType", MachineType, "Machine");
        await _params.SetAsync("MachineName", MachineName, "Machine");
        await _params.SetAsync("Station", Station, "Machine");
        await _params.SetAsync("LayoutJson", Json.Serialize(layout), "Machine");
        await ReloadAsync();
        StatusMessage = "机台配置已保存，主界面刷新后生效";
    }

    private async Task ImportTemplateAsync(TemplateInfo? info)
    {
        if (info == null) return;
        await ImportAsync(MachineTemplate.Load(info.Path));
    }

    private async Task ImportTemplateFileAsync()
    {
        var path = Dialogs.OpenFile("机型模板|*.json");
        if (path == null) return;
        await ImportAsync(MachineTemplate.Load(path));
    }

    private async Task ImportAsync(MachineTemplate template)
    {
        var tip = ReplaceDevices ? "将清空现有设备与点位，" : "";
        if (!Dialogs.Confirm($"导入机型模板「{template.Name}」？{tip}同编码的流程将生成新版本并发布，配方与 MES 接口将被覆盖。")) return;
        var signer = await SignAsync(Permissions.System.ImportTemplate, $"导入机型模板 {template.Name}");
        if (signer == null) return;
        var r = await _templates.ImportAsync(template, ReplaceDevices);
        await ReloadAsync();
        var msg = $"导入完成：设备 {r.Devices}，点位 {r.Tags}，流程 {r.Flows}，配方 {r.Recipes}，MES 接口 {r.MesInterfaces}";
        if (r.Warnings.Count > 0) msg += "\n警告：\n" + string.Join("\n", r.Warnings);
        Dialogs.ShowInfo(msg);
    }

    private async Task ExportTemplateAsync()
    {
        var path = Dialogs.SaveFile("机型模板|*.json", $"{MachineType}_{DateTime.Now:yyyyMMdd}.json");
        if (path == null) return;
        var t = await _templates.ExportAsync();
        await File.WriteAllTextAsync(path, Json.Serialize(t));
        StatusMessage = $"已导出到 {path}";
    }
}
