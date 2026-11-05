using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using XNY.Scada.Application.Production;
using XNY.Scada.Application.Settings;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Services;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>菜单项页面的视图模型。</summary>
/// <param name="key">键。</param>
/// <param name="title">标题。</param>
/// <param name="glyph">图标。</param>
/// <param name="permission">权限。</param>
/// <param name="viewModelType">视图模型类型。</param>
public sealed partial class MenuItemViewModel(string key, string title, string glyph, string permission, Type viewModelType) : ObservableObject
{
    /// <summary>菜单项键。</summary>
    public string Key { get; } = key;
    /// <summary>标题。</summary>
    public string Title { get; } = title;
    /// <summary>图标。</summary>
    public string Glyph { get; } = glyph;
    /// <summary>权限。</summary>
    public string Permission { get; } = permission;
    /// <summary>视图模型类型。</summary>
    public Type ViewModelType { get; } = viewModelType;

    /// <summary>是否可见。</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>是否选中。</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>主界面页面的视图模型。</summary>
public sealed partial class ShellViewModel : ViewModelBase, INavigationService
{
    /// <summary>服务。</summary>
    private readonly IServiceProvider _services;
    /// <summary>报警。</summary>
    private readonly IAlarmService _alarms;
    /// <summary>参数。</summary>
    private readonly SystemParamService _params;
    /// <summary>配方。</summary>
    private readonly RecipeService _recipes;
    /// <summary>空闲。</summary>
    private readonly IdleLogoutMonitor _idle;

    /// <summary>创建主界面视图模型。</summary>
    public ShellViewModel(
        ViewModelContext context,
        IServiceProvider services,
        LoginViewModel login,
        IAlarmService alarms,
        SystemParamService parameters,
        RecipeService recipes,
        IdleLogoutMonitor idle,
        UiUserInteraction interaction) : base(context)
    {
        _services = services;
        _alarms = alarms;
        _params = parameters;
        _recipes = recipes;
        _idle = idle;
        Login = login;

        MenuItems =
        [
            new("Dashboard", "主监控", "\uE80F", Permissions.Menu.Dashboard, typeof(DashboardViewModel)),
            new("Alarm", "报警中心", "\uE7BA", Permissions.Menu.Alarm, typeof(AlarmViewModel)),
            new("Production", "生产数据", "\uE9D2", Permissions.Menu.Production, typeof(ProductionViewModel)),
            new("Recipe", "配方与分档", "\uE8FD", Permissions.Menu.Recipe, typeof(RecipeViewModel)),
            new("FlowMonitor", "流程监控", "\uE9D9", Permissions.Menu.FlowMonitor, typeof(FlowMonitorViewModel)),
            new("FlowDesigner", "流程设计", "\uE8A5", Permissions.Menu.FlowDesigner, typeof(FlowDesignerViewModel)),
            new("Device", "设备配置", "\uE964", Permissions.Menu.Device, typeof(DeviceConfigViewModel)),
            new("Diagnostics", "通讯诊断", "\uE9F5", Permissions.Menu.Diagnostics, typeof(DiagnosticsViewModel)),
            new("Mes", "MES 配置", "\uE774", Permissions.Menu.Mes, typeof(MesViewModel)),
            new("User", "用户权限", "\uE716", Permissions.Menu.User, typeof(UserAdminViewModel)),
            new("Audit", "审计日志", "\uE81C", Permissions.Menu.Audit, typeof(AuditViewModel)),
            new("System", "系统参数", "\uE713", Permissions.Menu.System, typeof(SystemViewModel))
        ];

        login.LoggedIn += (_, _) =>
        {
            IsLoginVisible = false;
            UpdateUser();
        };
        Context.User.Changed += (_, _) => Ui.BeginInvoke(UpdateUser);
        _idle.AutoLoggedOut += (_, _) => Ui.BeginInvoke(() => Navigate("Dashboard"));
        _alarms.AlarmsChanged += (_, _) => Ui.BeginInvoke(UpdateAlarms);
        _recipes.CurrentRecipeChanged += (_, _) => Ui.BeginInvoke(() => OnPropertyChanged(nameof(RecipeName)));
        _params.ParamChanged += (_, _) => Ui.BeginInvoke(() => OnPropertyChanged(nameof(MachineTitle)));
        interaction.ConfirmRequested += (_, r) => PendingConfirmations.Add(r);
        interaction.ConfirmFinished += (_, r) => PendingConfirmations.Remove(r);
        interaction.Notified += (_, n) => StatusMessage = $"{DateTime.Now:HH:mm:ss} {n.Message}";

        ShowLoginCommand = new RelayCommand(() => IsLoginVisible = true);
        CloseLoginCommand = new RelayCommand(() => IsLoginVisible = false);
        LogoutCommand = new AsyncRelayCommand(async () =>
        {
            await Context.Auth.LogoutAsync("手动登出");
            Navigate("Dashboard");
        });
        ChangePasswordCommand = new AsyncRelayCommand(ChangePasswordAsync);
        NavigateCommand = new RelayCommand<MenuItemViewModel>(m => { if (m != null) Navigate(m.Key); });

        UpdateUser();
        UpdateAlarms();
        StartClock();
    }

    /// <summary>已导航发生变化时触发。</summary>
    public event EventHandler<string>? Navigated;

    /// <summary>标题。</summary>
    public override string Title => "XNY 新能源 SCADA";

    /// <summary>登录。</summary>
    public LoginViewModel Login { get; }

    /// <summary>菜单项。</summary>
    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    /// <summary>待处理确认。</summary>
    public ObservableCollection<ConfirmRequest> PendingConfirmations { get; } = [];

    /// <summary>当前键。</summary>
    public string? CurrentKey { get; private set; }

    /// <summary>当前页面。</summary>
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    /// <summary>是否登录可见。</summary>
    [ObservableProperty]
    private bool _isLoginVisible;

    /// <summary>用户显示。</summary>
    [ObservableProperty]
    private string _userDisplay = "未登录";

    /// <summary>是否已记录在。</summary>
    [ObservableProperty]
    private bool _isLoggedIn;

    /// <summary>当前时间。</summary>
    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    /// <summary>活动报警数量。</summary>
    [ObservableProperty]
    private int _activeAlarmCount;

    /// <summary>最新报警。</summary>
    [ObservableProperty]
    private string? _latestAlarm;

    /// <summary>机台标题。</summary>
    public string MachineTitle =>
        $"{_params.Get("MachineName", "SCADA")}  [{MachineTypes.DisplayNames.GetValueOrDefault(_params.MachineType, _params.MachineType)}]  工位 {_params.Station}";

    /// <summary>配方名称。</summary>
    public string RecipeName => _recipes.Current is { } r ? $"{r.Name} ({r.Code} v{r.Version})" : "未激活配方";

    /// <summary>显示登录命令。</summary>
    public IRelayCommand ShowLoginCommand { get; }
    /// <summary>关闭登录命令。</summary>
    public IRelayCommand CloseLoginCommand { get; }
    /// <summary>退出登录命令。</summary>
    public IAsyncRelayCommand LogoutCommand { get; }
    /// <summary>更改密码命令。</summary>
    public IAsyncRelayCommand ChangePasswordCommand { get; }
    /// <summary>导航命令。</summary>
    public IRelayCommand<MenuItemViewModel> NavigateCommand { get; }

    /// <summary>界面层在鼠标/键盘活动时调用，用于自动登出计时</summary>
    public void NotifyActivity() => _idle.NotifyActivity();

    /// <summary>导航。</summary>
    public void Navigate(string key)
    {
        var item = MenuItems.FirstOrDefault(m => m.Key == key);
        if (item == null) return;
        if (!item.IsVisible)
        {
            if (!IsLoggedIn) IsLoginVisible = true;
            else Dialogs.ShowError($"没有访问「{item.Title}」的权限");
            return;
        }
        var page = (ViewModelBase)_services.GetRequiredService(item.ViewModelType);
        if (ReferenceEquals(page, CurrentPage)) return;
        CurrentPage?.Deactivate();
        foreach (var m in MenuItems) m.IsSelected = m == item;
        CurrentKey = key;
        CurrentPage = page;
        _ = page.ActivateAsync();
        Navigated?.Invoke(this, key);
    }

    /// <summary>更新用户。</summary>
    private void UpdateUser()
    {
        IsLoggedIn = Context.User.IsAuthenticated;
        UserDisplay = IsLoggedIn
            ? $"{Context.User.User!.DisplayName} ({string.Join(",", Context.User.Roles.Select(r => r.Name))})"
            : "未登录";
        foreach (var m in MenuItems) m.IsVisible = m.Key == "Dashboard" || Can(m.Permission);
        if (CurrentKey != null && MenuItems.First(m => m.Key == CurrentKey) is { IsVisible: false }) Navigate("Dashboard");
        if (CurrentPage == null) Navigate("Dashboard");
    }

    /// <summary>更新报警。</summary>
    private void UpdateAlarms()
    {
        var active = _alarms.ActiveAlarms;
        ActiveAlarmCount = active.Count;
        LatestAlarm = active.FirstOrDefault() is { } a ? $"[{a.RaisedAt:HH:mm:ss}] {a.Message}" : null;
    }

    /// <summary>启动时钟。</summary>
    private void StartClock()
    {
        var timer = new Timer(_ => Ui.BeginInvoke(() =>
        {
            Now = DateTime.Now;
            var expired = PendingConfirmations.Where(c => c.Deadline < DateTime.Now.AddSeconds(-1)).ToList();
            foreach (var c in expired) PendingConfirmations.Remove(c);
        }), null, 1000, 1000);
        _clock = timer;
    }

    /// <summary>时钟。</summary>
    private Timer? _clock;

    /// <summary>更改密码。</summary>
    private async Task ChangePasswordAsync()
    {
        var change = Dialogs.RequestPasswordChange();
        if (change == null) return;
        var err = await Context.Auth.ChangePasswordAsync(change.OldPassword, change.NewPassword);
        if (err != null) Dialogs.ShowError(err);
        else Dialogs.ShowInfo("密码已修改");
    }

    /// <summary>释放资源。</summary>
    public override void Dispose()
    {
        _clock?.Dispose();
        base.Dispose();
    }
}
