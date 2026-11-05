using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Devices;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>报警页面的视图模型。</summary>
public sealed partial class AlarmViewModel : ViewModelBase
{
    /// <summary>报警。</summary>
    private readonly IAlarmService _alarms;
    /// <summary>查询。</summary>
    private readonly AlarmQueryService _query;
    /// <summary>审计。</summary>
    private readonly IAuditService _audit;

    /// <summary>创建报警视图模型。</summary>
    public AlarmViewModel(ViewModelContext context, IAlarmService alarms, AlarmQueryService query, IAuditService audit) : base(context)
    {
        _alarms = alarms;
        _query = query;
        _audit = audit;
        _alarms.AlarmsChanged += (_, _) => Ui.BeginInvoke(() => Replace(ActiveAlarms, _alarms.ActiveAlarms));
        QueryCommand = Command(QueryAsync);
        AcknowledgeCommand = Secured(Permissions.Alarm.Acknowledge, AcknowledgeAsync);
        AcknowledgeAllCommand = Secured(Permissions.Alarm.Acknowledge, AcknowledgeAllAsync);
    }

    /// <summary>标题。</summary>
    public override string Title => "报警中心";

    /// <summary>活动报警。</summary>
    public ObservableCollection<AlarmRecordEntity> ActiveAlarms { get; } = [];

    /// <summary>历史。</summary>
    public ObservableCollection<AlarmRecordEntity> History { get; } = [];

    /// <summary>选中报警。</summary>
    [ObservableProperty] private AlarmRecordEntity? _selectedAlarm;
    /// <summary>从。</summary>
    [ObservableProperty] private DateTime _from = DateTime.Today.AddDays(-7);
    /// <summary>到。</summary>
    [ObservableProperty] private DateTime _to = DateTime.Today.AddDays(1);
    /// <summary>关键字。</summary>
    [ObservableProperty] private string? _keyword;

    /// <summary>查询命令。</summary>
    public IAsyncRelayCommand QueryCommand { get; }
    /// <summary>确认命令。</summary>
    public IAsyncRelayCommand AcknowledgeCommand { get; }
    /// <summary>确认全部命令。</summary>
    public IAsyncRelayCommand AcknowledgeAllCommand { get; }

    /// <summary>处理激活。</summary>
    protected override async Task OnActivatedAsync()
    {
        Replace(ActiveAlarms, _alarms.ActiveAlarms);
        await QueryAsync();
    }

    /// <summary>查询。</summary>
    private async Task QueryAsync() => Replace(History, await _query.QueryAsync(From, To, Keyword));

    /// <summary>确认。</summary>
    private async Task AcknowledgeAsync()
    {
        if (SelectedAlarm == null) return;
        await _alarms.AcknowledgeAsync(SelectedAlarm.Id, Context.User.UserName);
        await _audit.LogAsync(AuditActions.AlarmAck, $"alarm:{SelectedAlarm.Id}", newValue: SelectedAlarm.Message);
        await QueryAsync();
    }

    /// <summary>确认全部。</summary>
    private async Task AcknowledgeAllAsync()
    {
        foreach (var a in _alarms.ActiveAlarms.Where(a => a.AcknowledgedAt == null))
        {
            await _alarms.AcknowledgeAsync(a.Id, Context.User.UserName);
        }
        await _audit.LogAsync(AuditActions.AlarmAck, "alarm:*", newValue: "全部确认");
        await QueryAsync();
    }
}
