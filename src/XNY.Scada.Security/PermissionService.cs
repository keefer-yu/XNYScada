namespace XNY.Scada.Security;

public interface IPermissionService
{
    event EventHandler? PermissionsChanged;

    bool Has(string permissionCode);

    void Demand(string permissionCode);

    /// <summary>数据权限：角色未配置该类型范围时视为全部可访问</summary>
    bool CanAccess(string scopeType, string scopeValue);
}

public sealed class PermissionService : IPermissionService
{
    private readonly UserSession _session;

    public PermissionService(UserSession session)
    {
        _session = session;
        _session.Changed += (_, _) => PermissionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? PermissionsChanged;

    public bool Has(string permissionCode) =>
        _session.IsAuthenticated && (_session.IsSuperAdmin || _session.PermissionCodes.Contains(permissionCode));

    public void Demand(string permissionCode)
    {
        if (!Has(permissionCode)) throw new PermissionDeniedException(permissionCode);
    }

    public bool CanAccess(string scopeType, string scopeValue)
    {
        if (!_session.IsAuthenticated) return false;
        if (_session.IsSuperAdmin) return true;
        var scopes = _session.DataScopes.Where(s => s.ScopeType.Equals(scopeType, StringComparison.OrdinalIgnoreCase)).ToList();
        return scopes.Count == 0 || scopes.Any(s => s.ScopeValue == "*" || s.ScopeValue.Equals(scopeValue, StringComparison.OrdinalIgnoreCase));
    }
}
