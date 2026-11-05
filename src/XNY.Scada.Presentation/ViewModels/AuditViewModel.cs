using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class AuditViewModel : ViewModelBase
{
    private readonly IAuditService _audit;

    public AuditViewModel(ViewModelContext context, IAuditService audit) : base(context)
    {
        _audit = audit;
        QueryCommand = Command(QueryAsync);
        ExportCommand = Command(ExportAsync, () => Logs.Count > 0);
        Logs.CollectionChanged += (_, _) => ExportCommand.NotifyCanExecuteChanged();
    }

    public override string Title => "审计日志";

    public IReadOnlyList<string> Actions { get; } =
    [
        "", AuditActions.Login, AuditActions.LoginFailed, AuditActions.Logout, AuditActions.ChangePassword, AuditActions.ParamChange,
        AuditActions.DeviceChange, AuditActions.ManualWrite, AuditActions.FlowSave, AuditActions.FlowPublish, AuditActions.RecipeChange,
        AuditActions.RecipeApprove, AuditActions.RecipeSwitch, AuditActions.UserChange, AuditActions.RoleChange, AuditActions.MesChange,
        AuditActions.TemplateImport, AuditActions.AlarmAck, AuditActions.Production
    ];

    public ObservableCollection<AuditLogEntity> Logs { get; } = [];

    [ObservableProperty] private DateTime _from = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _to = DateTime.Today.AddDays(1);
    [ObservableProperty] private string? _userName;
    [ObservableProperty] private string? _action;
    [ObservableProperty] private string? _keyword;
    [ObservableProperty] private AuditLogEntity? _selected;

    public IAsyncRelayCommand QueryCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }

    protected override Task OnActivatedAsync() => QueryAsync();

    private async Task QueryAsync() =>
        Replace(Logs, await _audit.QueryAsync(new AuditQuery(From, To, Blank(UserName), Blank(Action), Blank(Keyword), 5000)));

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task ExportAsync()
    {
        var path = Dialogs.SaveFile("CSV 文件|*.csv", $"audit_{From:yyyyMMdd}_{To:yyyyMMdd}.csv");
        if (path == null) return;
        var sb = new StringBuilder("时间,用户,动作,对象,原值,新值,签名人,原因,机器\n");
        foreach (var l in Logs)
        {
            sb.AppendLine(string.Join(",", new[] { l.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), l.UserName, l.Action, l.Target, l.OldValue, l.NewValue, l.Signer, l.Reason, l.Machine }.Select(Csv)));
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
        StatusMessage = $"已导出 {Logs.Count} 条";
    }

    private static string Csv(string? s) => s == null ? "" : s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
