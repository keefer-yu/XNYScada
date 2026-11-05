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

public sealed partial class MenuItemViewModel(string key, string title, string glyph, string permission, Type viewModelType) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public string Permission { get; } = permission;
    public Type ViewModelType { get; } = viewModelType;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class ShellViewModel : ViewModelBase, INavigationService
{
    private readonly IServiceProvider _services;
    private readonly IAlarmService _alarms;
    private readonly SystemParamService _params;
    private readonly RecipeService _recipes;
    private readonly IdleLogoutMonitor _idle;

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

    public event EventHandler<string>? Navigated;

    public override string Title => "XNY 新能源 SCADA";

    public LoginViewModel Login { get; }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public ObservableCollection<ConfirmRequest> PendingConfirmations { get; } = [];

    public string? CurrentKey { get; private set; }

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isLoginVisible;

    [ObservableProperty]
    private string _userDisplay = "未登录";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    [ObservableProperty]
    private int _activeAlarmCount;

    [ObservableProperty]
    private string? _latestAlarm;

    public string MachineTitle =>
        $"{_params.Get("MachineName", "SCADA")}  [{MachineTypes.DisplayNames.GetValueOrDefault(_params.MachineType, _params.MachineType)}]  工位 {_params.Station}";

    public string RecipeName => _recipes.Current is { } r ? $"{r.Name} ({r.Code} v{r.Version})" : "未激活配方";

    public IRelayCommand ShowLoginCommand { get; }
    public IRelayCommand CloseLoginCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand ChangePasswordCommand { get; }
    public IRelayCommand<MenuItemViewModel> NavigateCommand { get; }

    /// <summary>界面层在鼠标/键盘活动时调用，用于自动登出计时</summary>
    public void NotifyActivity() => _idle.NotifyActivity();

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

    private void UpdateAlarms()
    {
        var active = _alarms.ActiveAlarms;
        ActiveAlarmCount = active.Count;
        LatestAlarm = active.FirstOrDefault() is { } a ? $"[{a.RaisedAt:HH:mm:ss}] {a.Message}" : null;
    }

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

    private Timer? _clock;

    private async Task ChangePasswordAsync()
    {
        var change = Dialogs.RequestPasswordChange();
        if (change == null) return;
        var err = await Context.Auth.ChangePasswordAsync(change.OldPassword, change.NewPassword);
        if (err != null) Dialogs.ShowError(err);
        else Dialogs.ShowInfo("密码已修改");
    }

    public override void Dispose()
    {
        _clock?.Dispose();
        base.Dispose();
    }
}
