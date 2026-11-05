using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Devices;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class AlarmViewModel : ViewModelBase
{
    private readonly IAlarmService _alarms;
    private readonly AlarmQueryService _query;
    private readonly IAuditService _audit;

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

    public override string Title => "报警中心";

    public ObservableCollection<AlarmRecordEntity> ActiveAlarms { get; } = [];

    public ObservableCollection<AlarmRecordEntity> History { get; } = [];

    [ObservableProperty] private AlarmRecordEntity? _selectedAlarm;
    [ObservableProperty] private DateTime _from = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _to = DateTime.Today.AddDays(1);
    [ObservableProperty] private string? _keyword;

    public IAsyncRelayCommand QueryCommand { get; }
    public IAsyncRelayCommand AcknowledgeCommand { get; }
    public IAsyncRelayCommand AcknowledgeAllCommand { get; }

    protected override async Task OnActivatedAsync()
    {
        Replace(ActiveAlarms, _alarms.ActiveAlarms);
        await QueryAsync();
    }

    private async Task QueryAsync() => Replace(History, await _query.QueryAsync(From, To, Keyword));

    private async Task AcknowledgeAsync()
    {
        if (SelectedAlarm == null) return;
        await _alarms.AcknowledgeAsync(SelectedAlarm.Id, Context.User.UserName);
        await _audit.LogAsync(AuditActions.AlarmAck, $"alarm:{SelectedAlarm.Id}", newValue: SelectedAlarm.Message);
        await QueryAsync();
    }

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
