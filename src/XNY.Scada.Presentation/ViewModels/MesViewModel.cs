using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Mes;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class MesViewModel : ViewModelBase
{
    private readonly MesConfigService _service;

    public MesViewModel(ViewModelContext context, MesConfigService service) : base(context)
    {
        _service = service;
        NewCommand = Secured(Permissions.Mes.Edit, () =>
        {
            Selected = new MesInterfaceEntity { Code = $"Api{Interfaces.Count + 1}", Name = "新接口", Url = "http://mes-server/api/" };
            return Task.CompletedTask;
        });
        SaveCommand = Secured(Permissions.Mes.Edit, SaveAsync, () => Selected != null);
        DeleteCommand = Secured(Permissions.Mes.Edit, DeleteAsync, () => Selected is { Id: > 0 });
        TestCommand = Secured(Permissions.Mes.Test, TestAsync, () => Selected != null);
        QueryLogsCommand = Command(QueryLogsAsync);
        FlushOutboxCommand = Secured(Permissions.Mes.Edit, async () =>
        {
            var n = await _service.FlushOutboxAsync();
            StatusMessage = $"补传成功 {n} 条";
            await LoadOutboxAsync();
        });
        RequeueFailedCommand = Secured(Permissions.Mes.Edit, async () =>
        {
            await _service.RequeueFailedAsync();
            await LoadOutboxAsync();
        });
    }

    public override string Title => "MES 接口";

    public IReadOnlyList<string> Methods { get; } = ["POST", "GET", "PUT", "SOAP"];

    public IReadOnlyList<string> OutboxFilters { get; } = ["Pending", "Failed", "Sent"];

    public ObservableCollection<MesInterfaceEntity> Interfaces { get; } = [];
    public ObservableCollection<MesCallLogEntity> Logs { get; } = [];
    public ObservableCollection<MesOutboxEntity> Outbox { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DeleteCommand), nameof(TestCommand))]
    private MesInterfaceEntity? _selected;

    [ObservableProperty] private string _testArgs = "{\n  \"CellCode\": \"TEST0001\",\n  \"Station\": \"ST01\"\n}";
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private DateTime _logFrom = DateTime.Today;
    [ObservableProperty] private DateTime _logTo = DateTime.Today.AddDays(1);
    [ObservableProperty] private string _outboxFilter = "Pending";
    [ObservableProperty] private MesCallLogEntity? _selectedLog;

    public IAsyncRelayCommand NewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand TestCommand { get; }
    public IAsyncRelayCommand QueryLogsCommand { get; }
    public IAsyncRelayCommand FlushOutboxCommand { get; }
    public IAsyncRelayCommand RequeueFailedCommand { get; }

    protected override async Task OnActivatedAsync()
    {
        await ReloadAsync();
        await QueryLogsAsync();
        await LoadOutboxAsync();
    }

    partial void OnOutboxFilterChanged(string value)
    {
        if (IsActive) _ = RunAsync(LoadOutboxAsync);
    }

    private async Task ReloadAsync()
    {
        var id = Selected?.Id;
        Replace(Interfaces, await _service.ListAsync());
        Selected = Interfaces.FirstOrDefault(i => i.Id == id) ?? Interfaces.FirstOrDefault();
    }

    private async Task LoadOutboxAsync() =>
        Replace(Outbox, await _service.GetOutboxAsync(Enum.TryParse<MesOutboxStatus>(OutboxFilter, out var s) ? s : null));

    private async Task SaveAsync()
    {
        if (Selected == null) return;
        _ = Json.ToDictionary(Selected.HeadersJson);
        _ = Json.ToDictionary(Selected.ResponseMapJson);
        await _service.SaveAsync(Selected);
        await ReloadAsync();
        StatusMessage = "接口已保存";
    }

    private async Task DeleteAsync()
    {
        if (Selected == null || !Dialogs.Confirm($"删除 MES 接口 {Selected.Code}？引用它的流程节点将失败。")) return;
        await _service.DeleteAsync(Selected.Id);
        Selected = null;
        await ReloadAsync();
    }

    private async Task TestAsync()
    {
        if (Selected == null) return;
        var r = await _service.TestAsync(Selected, TestArgs);
        var fields = string.Join("\n", r.Fields.Select(kv => $"  {kv.Key} = {kv.Value}"));
        TestResult = $"结果：{(r.Success ? "成功" : "失败")}{(r.Offline ? "（离线）" : "")}{(r.Bypassed ? "（接口禁用，已旁路）" : "")}  耗时 {r.ElapsedMs}ms\n" +
                     $"消息：{r.Message}\n映射字段：\n{fields}\n原始响应：\n{r.RawResponse}";
        await QueryLogsAsync();
    }

    private async Task QueryLogsAsync() => Replace(Logs, await _service.GetLogsAsync(Selected?.Code, LogFrom, LogTo));
}
