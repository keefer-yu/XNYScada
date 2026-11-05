using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Mes;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>MES页面的视图模型。</summary>
public sealed partial class MesViewModel : ViewModelBase
{
    /// <summary>服务。</summary>
    private readonly MesConfigService _service;

    /// <summary>创建MES视图模型。</summary>
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

    /// <summary>标题。</summary>
    public override string Title => "MES 接口";

    /// <summary>方法。</summary>
    public IReadOnlyList<string> Methods { get; } = ["POST", "GET", "PUT", "SOAP"];

    /// <summary>发件箱筛选条件。</summary>
    public IReadOnlyList<string> OutboxFilters { get; } = ["Pending", "Failed", "Sent"];

    /// <summary>接口。</summary>
    public ObservableCollection<MesInterfaceEntity> Interfaces { get; } = [];
    /// <summary>日志。</summary>
    public ObservableCollection<MesCallLogEntity> Logs { get; } = [];
    /// <summary>发件箱。</summary>
    public ObservableCollection<MesOutboxEntity> Outbox { get; } = [];

    /// <summary>选中。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DeleteCommand), nameof(TestCommand))]
    private MesInterfaceEntity? _selected;

    /// <summary>测试参数。</summary>
    [ObservableProperty] private string _testArgs = "{\n  \"CellCode\": \"TEST0001\",\n  \"Station\": \"ST01\"\n}";
    /// <summary>测试结果。</summary>
    [ObservableProperty] private string? _testResult;
    /// <summary>日志从。</summary>
    [ObservableProperty] private DateTime _logFrom = DateTime.Today;
    /// <summary>日志到。</summary>
    [ObservableProperty] private DateTime _logTo = DateTime.Today.AddDays(1);
    /// <summary>发件箱筛选。</summary>
    [ObservableProperty] private string _outboxFilter = "Pending";
    /// <summary>选中日志。</summary>
    [ObservableProperty] private MesCallLogEntity? _selectedLog;

    /// <summary>新命令。</summary>
    public IAsyncRelayCommand NewCommand { get; }
    /// <summary>保存命令。</summary>
    public IAsyncRelayCommand SaveCommand { get; }
    /// <summary>删除命令。</summary>
    public IAsyncRelayCommand DeleteCommand { get; }
    /// <summary>测试命令。</summary>
    public IAsyncRelayCommand TestCommand { get; }
    /// <summary>查询日志命令。</summary>
    public IAsyncRelayCommand QueryLogsCommand { get; }
    /// <summary>刷出发件箱命令。</summary>
    public IAsyncRelayCommand FlushOutboxCommand { get; }
    /// <summary>重新排队失败命令。</summary>
    public IAsyncRelayCommand RequeueFailedCommand { get; }

    /// <summary>处理激活。</summary>
    protected override async Task OnActivatedAsync()
    {
        await ReloadAsync();
        await QueryLogsAsync();
        await LoadOutboxAsync();
    }

    /// <summary>当发件箱筛选变化时更新关联状态。</summary>
    partial void OnOutboxFilterChanged(string value)
    {
        if (IsActive) _ = RunAsync(LoadOutboxAsync);
    }

    /// <summary>重新加载。</summary>
    private async Task ReloadAsync()
    {
        var id = Selected?.Id;
        Replace(Interfaces, await _service.ListAsync());
        Selected = Interfaces.FirstOrDefault(i => i.Id == id) ?? Interfaces.FirstOrDefault();
    }

    /// <summary>加载发件箱。</summary>
    private async Task LoadOutboxAsync() =>
        Replace(Outbox, await _service.GetOutboxAsync(Enum.TryParse<MesOutboxStatus>(OutboxFilter, out var s) ? s : null));

    /// <summary>保存。</summary>
    private async Task SaveAsync()
    {
        if (Selected == null) return;
        _ = Json.ToDictionary(Selected.HeadersJson);
        _ = Json.ToDictionary(Selected.ResponseMapJson);
        await _service.SaveAsync(Selected);
        await ReloadAsync();
        StatusMessage = "接口已保存";
    }

    /// <summary>删除。</summary>
    private async Task DeleteAsync()
    {
        if (Selected == null || !Dialogs.Confirm($"删除 MES 接口 {Selected.Code}？引用它的流程节点将失败。")) return;
        await _service.DeleteAsync(Selected.Id);
        Selected = null;
        await ReloadAsync();
    }

    /// <summary>测试。</summary>
    private async Task TestAsync()
    {
        if (Selected == null) return;
        var r = await _service.TestAsync(Selected, TestArgs);
        var fields = string.Join("\n", r.Fields.Select(kv => $"  {kv.Key} = {kv.Value}"));
        TestResult = $"结果：{(r.Success ? "成功" : "失败")}{(r.Offline ? "（离线）" : "")}{(r.Bypassed ? "（接口禁用，已旁路）" : "")}  耗时 {r.ElapsedMs}ms\n" +
                     $"消息：{r.Message}\n映射字段：\n{fields}\n原始响应：\n{r.RawResponse}";
        await QueryLogsAsync();
    }

    /// <summary>查询日志。</summary>
    private async Task QueryLogsAsync() => Replace(Logs, await _service.GetLogsAsync(Selected?.Code, LogFrom, LogTo));
}
