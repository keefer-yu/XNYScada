using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

public sealed record DataScope(string ScopeType, string ScopeValue);

public interface ICurrentUser
{
    event EventHandler? Changed;

    bool IsAuthenticated { get; }

    UserEntity? User { get; }

    string UserName { get; }

    IReadOnlyList<RoleEntity> Roles { get; }

    IReadOnlySet<string> PermissionCodes { get; }

    IReadOnlyList<DataScope> DataScopes { get; }

    int MaxRoleLevel { get; }
}

public sealed class UserSession : ICurrentUser
{
    private readonly object _sync = new();

    public event EventHandler? Changed;

    public bool IsAuthenticated => User != null;

    public UserEntity? User { get; private set; }

    public string UserName => User?.UserName ?? "system";

    public IReadOnlyList<RoleEntity> Roles { get; private set; } = [];

    public IReadOnlySet<string> PermissionCodes { get; private set; } = new HashSet<string>();

    public IReadOnlyList<DataScope> DataScopes { get; private set; } = [];

    public int MaxRoleLevel => Roles.Count == 0 ? 0 : Roles.Max(r => r.Level);

    public bool IsSuperAdmin => Roles.Any(r => r.Code == BuiltInRoles.SuperAdmin);

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

public static class BuiltInRoles
{
    public const string Operator = "Operator";
    public const string Technician = "Technician";
    public const string Engineer = "Engineer";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
}
