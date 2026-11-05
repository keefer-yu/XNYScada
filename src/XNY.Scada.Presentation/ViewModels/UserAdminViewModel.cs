using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Device;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>检查项。</summary>
public sealed partial class CheckItem : ObservableObject
{
    /// <summary>检查键。</summary>
    public string Key { get; init; } = "";
    /// <summary>文本。</summary>
    public string Text { get; init; } = "";
    /// <summary>分组。</summary>
    public string Group { get; init; } = "";

    /// <summary>是否是否勾选。</summary>
    [ObservableProperty] private bool _isChecked;
}

/// <summary>用户行。</summary>
public sealed partial class UserRow : ObservableObject
{
    /// <summary>当前用户。</summary>
    public UserEntity User { get; init; } = new();

    /// <summary>角色标识。</summary>
    public IReadOnlyList<long> RoleIds { get; init; } = [];

    /// <summary>角色名称。</summary>
    [ObservableProperty] private string _roleNames = "";

    /// <summary>状态文本。</summary>
    public string StatusText => !User.IsEnabled ? "禁用" : User.LockedUntil > DateTime.Now ? "锁定" : "正常";
}

/// <summary>用户管理员页面的视图模型。</summary>
public sealed partial class UserAdminViewModel : ViewModelBase
{
    /// <summary>服务。</summary>
    private readonly IUserAdminService _service;
    /// <summary>设备。</summary>
    private readonly IDeviceManager _devices;

    /// <summary>创建用户管理员视图模型。</summary>
    public UserAdminViewModel(ViewModelContext context, IUserAdminService service, IDeviceManager devices) : base(context)
    {
        _service = service;
        _devices = devices;

        NewUserCommand = Secured(Permissions.User.Manage, () =>
        {
            SelectedUser = new UserRow { User = new UserEntity { UserName = "", DisplayName = "" } };
            return Task.CompletedTask;
        });
        SaveUserCommand = Secured(Permissions.User.Manage, SaveUserAsync, () => SelectedUser != null);
        DeleteUserCommand = Secured(Permissions.User.Manage, DeleteUserAsync, () => SelectedUser is { User.Id: > 0, User.IsBuiltIn: false });
        ResetPasswordCommand = Secured(Permissions.User.ResetPassword, ResetPasswordAsync, () => SelectedUser is { User.Id: > 0 });
        UnlockCommand = Secured(Permissions.User.Manage, async () =>
        {
            if (SelectedUser == null) return;
            await _service.UnlockAsync(SelectedUser.User.Id);
            await LoadUsersAsync();
        }, () => SelectedUser is { User.Id: > 0 });

        NewRoleCommand = Secured(Permissions.User.ManageRole, () =>
        {
            SelectedRole = new RoleEntity { Code = $"Role{Roles.Count + 1}", Name = "新角色", Level = 10 };
            return Task.CompletedTask;
        });
        SaveRoleCommand = Secured(Permissions.User.ManageRole, SaveRoleAsync, () => SelectedRole != null);
        DeleteRoleCommand = Secured(Permissions.User.ManageRole, DeleteRoleAsync, () => SelectedRole is { Id: > 0, IsBuiltIn: false });
        SaveRolePermissionsCommand = Secured(Permissions.User.ManageRole, SaveRolePermissionsAsync, () => SelectedRole is { Id: > 0 });
        CheckAllCommand = Command(() =>
        {
            foreach (var p in PermissionItems) p.IsChecked = Can(p.Key);
            return Task.CompletedTask;
        });
        UncheckAllCommand = Command(() =>
        {
            foreach (var p in PermissionItems) p.IsChecked = false;
            return Task.CompletedTask;
        });
    }

    /// <summary>标题。</summary>
    public override string Title => "用户权限";

    /// <summary>用户。</summary>
    public ObservableCollection<UserRow> Users { get; } = [];
    /// <summary>当前角色。</summary>
    public ObservableCollection<RoleEntity> Roles { get; } = [];
    /// <summary>用户角色项。</summary>
    public ObservableCollection<CheckItem> UserRoleItems { get; } = [];
    /// <summary>权限项。</summary>
    public ObservableCollection<CheckItem> PermissionItems { get; } = [];
    /// <summary>数据范围项。</summary>
    public ObservableCollection<CheckItem> DataScopeItems { get; } = [];

    /// <summary>选中用户。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveUserCommand), nameof(DeleteUserCommand), nameof(ResetPasswordCommand), nameof(UnlockCommand))]
    private UserRow? _selectedUser;

    /// <summary>选中角色。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRoleCommand), nameof(DeleteRoleCommand), nameof(SaveRolePermissionsCommand))]
    private RoleEntity? _selectedRole;

    /// <summary>用户名称。</summary>
    [ObservableProperty] private string _userName = "";
    /// <summary>显示名称。</summary>
    [ObservableProperty] private string _displayName = "";
    /// <summary>卡不。</summary>
    [ObservableProperty] private string? _cardNo;
    /// <summary>用户启用。</summary>
    [ObservableProperty] private bool _userEnabled = true;
    /// <summary>新密码。</summary>
    [ObservableProperty] private string? _newPassword;

    /// <summary>新用户命令。</summary>
    public IAsyncRelayCommand NewUserCommand { get; }
    /// <summary>保存用户命令。</summary>
    public IAsyncRelayCommand SaveUserCommand { get; }
    /// <summary>删除用户命令。</summary>
    public IAsyncRelayCommand DeleteUserCommand { get; }
    /// <summary>重置密码命令。</summary>
    public IAsyncRelayCommand ResetPasswordCommand { get; }
    /// <summary>解锁命令。</summary>
    public IAsyncRelayCommand UnlockCommand { get; }
    /// <summary>新角色命令。</summary>
    public IAsyncRelayCommand NewRoleCommand { get; }
    /// <summary>保存角色命令。</summary>
    public IAsyncRelayCommand SaveRoleCommand { get; }
    /// <summary>删除角色命令。</summary>
    public IAsyncRelayCommand DeleteRoleCommand { get; }
    /// <summary>保存角色权限命令。</summary>
    public IAsyncRelayCommand SaveRolePermissionsCommand { get; }
    /// <summary>检查全部命令。</summary>
    public IAsyncRelayCommand CheckAllCommand { get; }
    /// <summary>取消勾选全部命令。</summary>
    public IAsyncRelayCommand UncheckAllCommand { get; }

    /// <summary>处理激活。</summary>
    protected override async Task OnActivatedAsync()
    {
        await LoadRolesAsync();
        await LoadUsersAsync();
    }

    /// <summary>加载角色。</summary>
    private async Task LoadRolesAsync()
    {
        var id = SelectedRole?.Id;
        Replace(Roles, await _service.GetRolesAsync());
        SelectedRole = Roles.FirstOrDefault(r => r.Id == id) ?? Roles.FirstOrDefault();
    }

    /// <summary>加载用户。</summary>
    private async Task LoadUsersAsync()
    {
        var id = SelectedUser?.User.Id;
        var users = await _service.GetUsersAsync();
        Replace(Users, users.Select(u => new UserRow
        {
            User = u.User,
            RoleIds = u.RoleIds,
            RoleNames = string.Join(",", Roles.Where(r => u.RoleIds.Contains(r.Id)).Select(r => r.Name))
        }));
        SelectedUser = Users.FirstOrDefault(u => u.User.Id == id) ?? Users.FirstOrDefault();
    }

    /// <summary>当选中用户变化时更新关联状态。</summary>
    partial void OnSelectedUserChanged(UserRow? value)
    {
        UserName = value?.User.UserName ?? "";
        DisplayName = value?.User.DisplayName ?? "";
        CardNo = value?.User.CardNo;
        UserEnabled = value?.User.IsEnabled ?? true;
        NewPassword = null;
        Replace(UserRoleItems, Roles.Select(r => new CheckItem
        {
            Key = r.Id.ToString(), Text = $"{r.Name}（{r.Code}，等级 {r.Level}）", IsChecked = value?.RoleIds.Contains(r.Id) == true
        }));
    }

    /// <summary>当选中角色变化时更新关联状态。</summary>
    partial void OnSelectedRoleChanged(RoleEntity? value) => _ = RunAsync(() => LoadRoleDetailAsync(value));

    /// <summary>加载角色详情。</summary>
    private async Task LoadRoleDetailAsync(RoleEntity? role)
    {
        var perms = await _service.GetPermissionsAsync();
        var granted = role is { Id: > 0 } ? (await _service.GetRolePermissionsAsync(role.Id)).ToHashSet() : [];
        var isSuper = role?.Code == BuiltInRoles.SuperAdmin;
        Replace(PermissionItems, perms.Select(p => new CheckItem
        {
            Key = p.Code, Text = $"{p.Name}  [{p.Code}]", Group = p.Category, IsChecked = isSuper || granted.Contains(p.Code)
        }));

        var scopes = role is { Id: > 0 } ? await _service.GetRoleDataScopesAsync(role.Id) : [];
        var devices = _devices.GetStatuses().Select(s => s.DeviceName);
        Replace(DataScopeItems, devices.Select(d => new CheckItem
        {
            Key = d, Text = d, Group = "Device", IsChecked = scopes.Any(s => s.ScopeType == "Device" && (s.ScopeValue == d || s.ScopeValue == "*"))
        }));
    }

    /// <summary>保存用户。</summary>
    private async Task SaveUserAsync()
    {
        if (SelectedUser == null) return;
        var u = SelectedUser.User;
        var entity = new UserEntity
        {
            Id = u.Id, UserName = UserName.Trim(), DisplayName = DisplayName.Trim(), CardNo = string.IsNullOrWhiteSpace(CardNo) ? null : CardNo.Trim(),
            IsEnabled = UserEnabled, CreatedAt = u.CreatedAt, PasswordChangedAt = u.PasswordChangedAt, LastLoginAt = u.LastLoginAt
        };
        var roleIds = UserRoleItems.Where(i => i.IsChecked).Select(i => long.Parse(i.Key)).ToList();
        var saved = await _service.SaveUserAsync(entity, roleIds, string.IsNullOrEmpty(NewPassword) ? null : NewPassword);
        SelectedUser = new UserRow { User = saved, RoleIds = roleIds };
        await LoadUsersAsync();
        StatusMessage = "用户已保存";
    }

    /// <summary>删除用户。</summary>
    private async Task DeleteUserAsync()
    {
        if (SelectedUser == null || !Dialogs.Confirm($"删除用户 {SelectedUser.User.UserName}？")) return;
        await _service.DeleteUserAsync(SelectedUser.User.Id);
        SelectedUser = null;
        await LoadUsersAsync();
    }

    /// <summary>重置枚举密码。</summary>
    private async Task ResetPasswordAsync()
    {
        if (SelectedUser == null) return;
        var pwd = Dialogs.Prompt($"为 {SelectedUser.User.UserName} 设置临时密码（下次登录需修改）", "重置密码", "");
        if (string.IsNullOrEmpty(pwd)) return;
        await _service.ResetPasswordAsync(SelectedUser.User.Id, pwd);
        StatusMessage = "密码已重置";
    }

    /// <summary>保存角色。</summary>
    private async Task SaveRoleAsync()
    {
        if (SelectedRole == null) return;
        var saved = await _service.SaveRoleAsync(SelectedRole);
        SelectedRole = saved;
        await LoadRolesAsync();
        StatusMessage = "角色已保存";
    }

    /// <summary>删除角色。</summary>
    private async Task DeleteRoleAsync()
    {
        if (SelectedRole == null || !Dialogs.Confirm($"删除角色 {SelectedRole.Name}？已分配的用户将失去该角色。")) return;
        await _service.DeleteRoleAsync(SelectedRole.Id);
        SelectedRole = null;
        await LoadRolesAsync();
        await LoadUsersAsync();
    }

    /// <summary>保存角色权限。</summary>
    private async Task SaveRolePermissionsAsync()
    {
        if (SelectedRole is not { Id: > 0 } role) return;
        if (role.Code != BuiltInRoles.SuperAdmin)
        {
            await _service.SetRolePermissionsAsync(role.Id, PermissionItems.Where(p => p.IsChecked).Select(p => p.Key).ToList());
        }
        var scopes = DataScopeItems.Where(d => d.IsChecked).Select(d => new DataScope(d.Group, d.Key)).ToList();
        // 全选等同于不限制
        if (scopes.Count == DataScopeItems.Count) scopes.Clear();
        await _service.SetRoleDataScopesAsync(role.Id, scopes);
        StatusMessage = "角色权限已保存，相关用户重新登录后生效";
    }
}
