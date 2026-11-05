namespace XNY.Scada.Security;

/// <summary>当前登录用户的权限判断。超级管理员跳过权限点检查。</summary>
public interface IPermissionService
{
    /// <summary>权限发生变化时触发。</summary>
    event EventHandler? PermissionsChanged;

    /// <summary>是否有。</summary>
    bool Has(string permissionCode);

    /// <summary>要求具备。</summary>
    void Demand(string permissionCode);

    /// <summary>数据权限：角色未配置该类型范围时视为全部可访问</summary>
    bool CanAccess(string scopeType, string scopeValue);
}

/// <summary>权限服务。</summary>
public sealed class PermissionService : IPermissionService
{
    /// <summary>会话。</summary>
    private readonly UserSession _session;

    /// <summary>创建权限服务。</summary>
    public PermissionService(UserSession session)
    {
        _session = session;
        _session.Changed += (_, _) => PermissionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>权限发生变化时触发。</summary>
    public event EventHandler? PermissionsChanged;

    /// <summary>是否有。</summary>
    public bool Has(string permissionCode) =>
        _session.IsAuthenticated && (_session.IsSuperAdmin || _session.PermissionCodes.Contains(permissionCode));

    /// <summary>要求具备。</summary>
    public void Demand(string permissionCode)
    {
        if (!Has(permissionCode)) throw new PermissionDeniedException(permissionCode);
    }

    /// <summary>能否访问。</summary>
    public bool CanAccess(string scopeType, string scopeValue)
    {
        if (!_session.IsAuthenticated) return false;
        if (_session.IsSuperAdmin) return true;
        var scopes = _session.DataScopes.Where(s => s.ScopeType.Equals(scopeType, StringComparison.OrdinalIgnoreCase)).ToList();
        return scopes.Count == 0 || scopes.Any(s => s.ScopeValue == "*" || s.ScopeValue.Equals(scopeValue, StringComparison.OrdinalIgnoreCase));
    }
}
