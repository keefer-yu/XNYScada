using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>ViewModel 公共依赖</summary>
public sealed class ViewModelContext(
    IPermissionService permissions,
    IDialogService dialogs,
    IUiDispatcher ui,
    IAuthService auth,
    ICurrentUser user,
    ILoggerFactory loggerFactory)
{
    public IPermissionService Permissions { get; } = permissions;
    public IDialogService Dialogs { get; } = dialogs;
    public IUiDispatcher Ui { get; } = ui;
    public IAuthService Auth { get; } = auth;
    public ICurrentUser User { get; } = user;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}

public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    private readonly List<(IRelayCommand Command, string Permission)> _securedCommands = [];
    private Timer? _refreshTimer;
    private int _refreshing;

    protected ViewModelBase(ViewModelContext context)
    {
        Context = context;
        Logger = context.LoggerFactory.CreateLogger(GetType());
        Context.Permissions.PermissionsChanged += OnPermissionsChanged;
    }

    protected ViewModelContext Context { get; }

    protected ILogger Logger { get; }

    protected IDialogService Dialogs => Context.Dialogs;

    protected IUiDispatcher Ui => Context.Ui;

    public virtual string Title => GetType().Name;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsActive { get; private set; }

    public bool Can(string permission) => Context.Permissions.Has(permission);

    public async Task ActivateAsync()
    {
        IsActive = true;
        await RunAsync(OnActivatedAsync);
    }

    public void Deactivate()
    {
        IsActive = false;
        StopRefresh();
        OnDeactivated();
    }

    protected virtual Task OnActivatedAsync() => Task.CompletedTask;

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

    protected IAsyncRelayCommand<T> Secured<T>(string permission, Func<T?, Task> execute)
    {
        var cmd = new AsyncRelayCommand<T>(p => RunAsync(() => execute(p)), _ => Can(permission));
        _securedCommands.Add((cmd, permission));
        return cmd;
    }

    protected IAsyncRelayCommand Command(Func<Task> execute, Func<bool>? canExecute = null) =>
        new AsyncRelayCommand(() => RunAsync(execute), canExecute ?? (() => true));

    protected void RefreshCommands()
    {
        foreach (var (c, _) in _securedCommands) c.NotifyCanExecuteChanged();
    }

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

    protected void StopRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    protected static void Replace<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    public virtual void Dispose()
    {
        StopRefresh();
        Context.Permissions.PermissionsChanged -= OnPermissionsChanged;
        GC.SuppressFinalize(this);
    }
}
