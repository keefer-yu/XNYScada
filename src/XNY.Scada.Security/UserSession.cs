using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

/// <summary>数据范围。</summary>
/// <param name="ScopeType">数据范围类型。</param>
/// <param name="ScopeValue">数据范围值。</param>
public sealed record DataScope(string ScopeType, string ScopeValue);

/// <summary>当前用户的契约。</summary>
public interface ICurrentUser
{
    /// <summary>成员发生变化时触发。</summary>
    event EventHandler? Changed;

    /// <summary>是否已登录。</summary>
    bool IsAuthenticated { get; }

    /// <summary>当前用户。</summary>
    UserEntity? User { get; }

    /// <summary>用户名。</summary>
    string UserName { get; }

    /// <summary>当前角色。</summary>
    IReadOnlyList<RoleEntity> Roles { get; }

    /// <summary>当前拥有的权限编码。</summary>
    IReadOnlySet<string> PermissionCodes { get; }

    /// <summary>当前数据范围。</summary>
    IReadOnlyList<DataScope> DataScopes { get; }

    /// <summary>最大角色级别。</summary>
    int MaxRoleLevel { get; }
}

/// <summary>用户会话。</summary>
public sealed class UserSession : ICurrentUser
{
    /// <summary>同步。</summary>
    private readonly object _sync = new();

    /// <summary>成员发生变化时触发。</summary>
    public event EventHandler? Changed;

    /// <summary>是否已登录。</summary>
    public bool IsAuthenticated => User != null;

    /// <summary>当前用户。</summary>
    public UserEntity? User { get; private set; }

    /// <summary>用户名。</summary>
    public string UserName => User?.UserName ?? "system";

    /// <summary>当前角色。</summary>
    public IReadOnlyList<RoleEntity> Roles { get; private set; } = [];

    /// <summary>当前拥有的权限编码。</summary>
    public IReadOnlySet<string> PermissionCodes { get; private set; } = new HashSet<string>();

    /// <summary>当前数据范围。</summary>
    public IReadOnlyList<DataScope> DataScopes { get; private set; } = [];

    /// <summary>最大角色级别。</summary>
    public int MaxRoleLevel => Roles.Count == 0 ? 0 : Roles.Max(r => r.Level);

    /// <summary>是否为超级管理员。</summary>
    public bool IsSuperAdmin => Roles.Any(r => r.Code == BuiltInRoles.SuperAdmin);

    /// <summary>签名在。</summary>
    internal void SignIn(UserEntity user, IReadOnlyList<RoleEntity> roles, IEnumerable<string> permissions, IReadOnlyList<DataScope> scopes)
    {
        lock (_sync)
        {
            User = user;
            Roles = roles;
            PermissionCodes = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            DataScopes = scopes;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>签名超出。</summary>
    internal void SignOut()
    {
        lock (_sync)
        {
            User = null;
            Roles = [];
            PermissionCodes = new HashSet<string>();
            DataScopes = [];
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>启动时写入且不可删除的角色编码。级别越高，可分配的角色范围越大。</summary>
public static class BuiltInRoles
{
    /// <summary>操作员。</summary>
    public const string Operator = "Operator";
    /// <summary>技术员。</summary>
    public const string Technician = "Technician";
    /// <summary>工程师。</summary>
    public const string Engineer = "Engineer";
    /// <summary>管理员。</summary>
    public const string Admin = "Admin";
    /// <summary>超级管理员。</summary>
    public const string SuperAdmin = "SuperAdmin";
}
