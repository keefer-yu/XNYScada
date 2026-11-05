using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>ViewModel 公共依赖</summary>
/// <param name="permissions">权限。</param>
/// <param name="dialogs">对话框。</param>
/// <param name="ui">界面。</param>
/// <param name="auth">认证。</param>
/// <param name="user">用户。</param>
/// <param name="loggerFactory">日志工厂。</param>
public sealed class ViewModelContext(
    IPermissionService permissions,
    IDialogService dialogs,
    IUiDispatcher ui,
    IAuthService auth,
    ICurrentUser user,
    ILoggerFactory loggerFactory)
{
    /// <summary>权限。</summary>
    public IPermissionService Permissions { get; } = permissions;
    /// <summary>对话框。</summary>
    public IDialogService Dialogs { get; } = dialogs;
    /// <summary>界面。</summary>
    public IUiDispatcher Ui { get; } = ui;
    /// <summary>认证。</summary>
    public IAuthService Auth { get; } = auth;
    /// <summary>当前用户。</summary>
    public ICurrentUser User { get; } = user;
    /// <summary>日志工厂。</summary>
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}

/// <summary>视图模型基类。</summary>
public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    /// <summary>受保护命令。</summary>
    private readonly List<(IRelayCommand Command, string Permission)> _securedCommands = [];
    /// <summary>刷新定时器。</summary>
    private Timer? _refreshTimer;
    /// <summary>正在刷新。</summary>
    private int _refreshing;

    /// <summary>创建视图模型基类。</summary>
    protected ViewModelBase(ViewModelContext context)
    {
        Context = context;
        Logger = context.LoggerFactory.CreateLogger(GetType());
        Context.Permissions.PermissionsChanged += OnPermissionsChanged;
    }

    /// <summary>上下文。</summary>
    protected ViewModelContext Context { get; }

    /// <summary>日志。</summary>
    protected ILogger Logger { get; }

    /// <summary>对话框。</summary>
    protected IDialogService Dialogs => Context.Dialogs;

    /// <summary>界面。</summary>
    protected IUiDispatcher Ui => Context.Ui;

    /// <summary>标题。</summary>
    public virtual string Title => GetType().Name;

    /// <summary>是否忙碌。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>状态消息。</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>是否仍处于活动状态。</summary>
    public bool IsActive { get; private set; }

    /// <summary>能否。</summary>
    public bool Can(string permission) => Context.Permissions.Has(permission);

    /// <summary>激活。</summary>
    public async Task ActivateAsync()
    {
        IsActive = true;
        await RunAsync(OnActivatedAsync);
    }

    /// <summary>停用。</summary>
    public void Deactivate()
    {
        IsActive = false;
        StopRefresh();
        OnDeactivated();
    }

    /// <summary>处理激活。</summary>
    protected virtual Task OnActivatedAsync() => Task.CompletedTask;

    /// <summary>处理已停用。</summary>
    protected virtual void OnDeactivated()
    {
    }

    /// <summary>执行操作并统一处理忙碌状态与异常提示</summary>
    protected async Task RunAsync(Func<Task> action, string? successMessage = null)
    {
        IsBusy = true;
        try
        {
            await action();
            if (successMessage != null) StatusMessage = $"{DateTime.Now:HH:mm:ss} {successMessage}";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "操作失败");
            StatusMessage = ex.Message;
            Dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>创建带权限控制的命令：无权限时 CanExecute 为 false，权限变化时自动刷新</summary>
    protected IAsyncRelayCommand Secured(string permission, Func<Task> execute, Func<bool>? canExecute = null)
    {
        var cmd = new AsyncRelayCommand(() => RunAsync(execute), () => Can(permission) && (canExecute?.Invoke() ?? true));
        _securedCommands.Add((cmd, permission));
        return cmd;
    }

    /// <summary>受保护。</summary>
    protected IAsyncRelayCommand<T> Secured<T>(string permission, Func<T?, Task> execute)
    {
        var cmd = new AsyncRelayCommand<T>(p => RunAsync(() => execute(p)), _ => Can(permission));
        _securedCommands.Add((cmd, permission));
        return cmd;
    }

    /// <summary>绑定的命令。</summary>
    protected IAsyncRelayCommand Command(Func<Task> execute, Func<bool>? canExecute = null) =>
        new AsyncRelayCommand(() => RunAsync(execute), canExecute ?? (() => true));

    /// <summary>刷新命令。</summary>
    protected void RefreshCommands()
    {
        foreach (var (c, _) in _securedCommands) c.NotifyCanExecuteChanged();
    }

    /// <summary>当权限变化时更新关联状态。</summary>
    private void OnPermissionsChanged(object? sender, EventArgs e) => Ui.BeginInvoke(() =>
    {
        RefreshCommands();
        OnPropertyChanged(string.Empty);
    });

    /// <summary>电子签名：弹出签名框并校验签名人是否具备指定权限，返回签名人；取消或失败返回 null</summary>
    protected async Task<string?> SignAsync(string permission, string reason)
    {
        var credential = Dialogs.RequestSignature(reason);
        if (credential == null) return null;
        var r = await Context.Auth.VerifySignatureAsync(credential.UserName, credential.Password, permission);
        if (!r.Success)
        {
            Dialogs.ShowError(r.Message, "电子签名失败");
            return null;
        }
        return r.Signer;
    }

    /// <summary>页面激活期间周期刷新（在 UI 线程执行，上一轮未完成时跳过）</summary>
    protected void StartRefresh(TimeSpan interval, Func<Task> refresh)
    {
        StopRefresh();
        _refreshTimer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
            Ui.BeginInvoke(async () =>
            {
                try
                {
                    if (IsActive) await refresh();
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "刷新失败");
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshing, 0);
                }
            });
        }, null, interval, interval);
    }

    /// <summary>停止刷新。</summary>
    protected void StopRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    /// <summary>替换。</summary>
    protected static void Replace<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    /// <summary>释放资源。</summary>
    public virtual void Dispose()
    {
        StopRefresh();
        Context.Permissions.PermissionsChanged -= OnPermissionsChanged;
        GC.SuppressFinalize(this);
    }
}
