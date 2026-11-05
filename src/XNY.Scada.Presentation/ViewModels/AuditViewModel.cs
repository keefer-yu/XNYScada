using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>审计页面的视图模型。</summary>
public sealed partial class AuditViewModel : ViewModelBase
{
    /// <summary>审计。</summary>
    private readonly IAuditService _audit;

    /// <summary>创建审计视图模型。</summary>
    public AuditViewModel(ViewModelContext context, IAuditService audit) : base(context)
    {
        _audit = audit;
        QueryCommand = Command(QueryAsync);
        ExportCommand = Command(ExportAsync, () => Logs.Count > 0);
        Logs.CollectionChanged += (_, _) => ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>标题。</summary>
    public override string Title => "审计日志";

    /// <summary>动作。</summary>
    public IReadOnlyList<string> Actions { get; } =
    [
        "", AuditActions.Login, AuditActions.LoginFailed, AuditActions.Logout, AuditActions.ChangePassword, AuditActions.ParamChange,
        AuditActions.DeviceChange, AuditActions.ManualWrite, AuditActions.FlowSave, AuditActions.FlowPublish, AuditActions.RecipeChange,
        AuditActions.RecipeApprove, AuditActions.RecipeSwitch, AuditActions.UserChange, AuditActions.RoleChange, AuditActions.MesChange,
        AuditActions.TemplateImport, AuditActions.AlarmAck, AuditActions.Production
    ];

    /// <summary>日志。</summary>
    public ObservableCollection<AuditLogEntity> Logs { get; } = [];

    /// <summary>从。</summary>
    [ObservableProperty] private DateTime _from = DateTime.Today.AddDays(-7);
    /// <summary>到。</summary>
    [ObservableProperty] private DateTime _to = DateTime.Today.AddDays(1);
    /// <summary>用户名称。</summary>
    [ObservableProperty] private string? _userName;
    /// <summary>动作。</summary>
    [ObservableProperty] private string? _action;
    /// <summary>关键字。</summary>
    [ObservableProperty] private string? _keyword;
    /// <summary>选中。</summary>
    [ObservableProperty] private AuditLogEntity? _selected;

    /// <summary>查询命令。</summary>
    public IAsyncRelayCommand QueryCommand { get; }
    /// <summary>导出命令。</summary>
    public IAsyncRelayCommand ExportCommand { get; }

    /// <summary>处理激活。</summary>
    protected override Task OnActivatedAsync() => QueryAsync();

    /// <summary>查询。</summary>
    private async Task QueryAsync() =>
        Replace(Logs, await _audit.QueryAsync(new AuditQuery(From, To, Blank(UserName), Blank(Action), Blank(Keyword), 5000)));

    /// <summary>空白。</summary>
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>导出。</summary>
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

    /// <summary>CSV。</summary>
    private static string Csv(string? s) => s == null ? "" : s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
