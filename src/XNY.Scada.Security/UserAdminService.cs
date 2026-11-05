using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

/// <summary>用户信息。</summary>
/// <param name="User">当前用户。</param>
/// <param name="RoleIds">角色标识。</param>
public sealed record UserInfo(UserEntity User, IReadOnlyList<long> RoleIds);

/// <summary>I用户管理员服务。</summary>
public interface IUserAdminService
{
    /// <summary>获取用户。</summary>
    Task<List<UserInfo>> GetUsersAsync();

    /// <summary>获取角色。</summary>
    Task<List<RoleEntity>> GetRolesAsync();

    /// <summary>获取权限。</summary>
    Task<List<PermissionEntity>> GetPermissionsAsync();

    /// <summary>获取角色权限。</summary>
    Task<List<string>> GetRolePermissionsAsync(long roleId);

    /// <summary>获取角色数据范围。</summary>
    Task<List<RoleDataScopeEntity>> GetRoleDataScopesAsync(long roleId);

    /// <summary>保存用户。</summary>
    Task<UserEntity> SaveUserAsync(UserEntity user, IReadOnlyList<long> roleIds, string? newPassword);

    /// <summary>删除用户。</summary>
    Task DeleteUserAsync(long userId);

    /// <summary>重置枚举密码。</summary>
    Task ResetPasswordAsync(long userId, string newPassword);

    /// <summary>解锁。</summary>
    Task UnlockAsync(long userId);

    /// <summary>保存角色。</summary>
    Task<RoleEntity> SaveRoleAsync(RoleEntity role);

    /// <summary>删除角色。</summary>
    Task DeleteRoleAsync(long roleId);

    /// <summary>设置角色权限。</summary>
    Task SetRolePermissionsAsync(long roleId, IReadOnlyCollection<string> permissionCodes);

    /// <summary>设置角色数据范围。</summary>
    Task SetRoleDataScopesAsync(long roleId, IReadOnlyCollection<DataScope> scopes);
}

/// <summary>用户管理员服务。</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="permissions">权限。</param>
/// <param name="currentUser">当前用户。</param>
/// <param name="audit">审计。</param>
/// <param name="options">配置项。</param>
public sealed class UserAdminService(
    ISqlSugarClient db,
    IPermissionService permissions,
    ICurrentUser currentUser,
    IAuditService audit,
    SecurityOptions options) : IUserAdminService
{
    /// <summary>获取用户。</summary>
    public async Task<List<UserInfo>> GetUsersAsync()
    {
        var users = await db.Queryable<UserEntity>().OrderBy(u => u.Id).ToListAsync();
        var links = await db.Queryable<UserRoleEntity>().ToListAsync();
        return users.Select(u => new UserInfo(u, links.Where(l => l.UserId == u.Id).Select(l => l.RoleId).ToList())).ToList();
    }

    /// <summary>获取角色。</summary>
    public Task<List<RoleEntity>> GetRolesAsync() => db.Queryable<RoleEntity>().OrderBy(r => r.Level).ToListAsync();

    /// <summary>获取权限。</summary>
    public Task<List<PermissionEntity>> GetPermissionsAsync() => db.Queryable<PermissionEntity>().OrderBy(p => p.Sort).ToListAsync();

    /// <summary>获取角色权限。</summary>
    public Task<List<string>> GetRolePermissionsAsync(long roleId) =>
        db.Queryable<RolePermissionEntity>().Where(r => r.RoleId == roleId).Select(r => r.PermissionCode).ToListAsync();

    /// <summary>获取角色数据范围。</summary>
    public Task<List<RoleDataScopeEntity>> GetRoleDataScopesAsync(long roleId) =>
        db.Queryable<RoleDataScopeEntity>().Where(r => r.RoleId == roleId).ToListAsync();

    /// <summary>保存用户。</summary>
    public async Task<UserEntity> SaveUserAsync(UserEntity user, IReadOnlyList<long> roleIds, string? newPassword)
    {
        permissions.Demand(Permissions.User.Manage);
        if (string.IsNullOrWhiteSpace(user.UserName)) throw new ScadaSecurityException("用户名不能为空");
        if (await db.Queryable<UserEntity>().AnyAsync(u => u.UserName == user.UserName && u.Id != user.Id))
            throw new ScadaSecurityException($"用户名 {user.UserName} 已存在");
        if (!string.IsNullOrWhiteSpace(user.CardNo) && await db.Queryable<UserEntity>().AnyAsync(u => u.CardNo == user.CardNo && u.Id != user.Id))
            throw new ScadaSecurityException("卡号已绑定其他用户");

        await EnsureCanAssignRolesAsync(roleIds);
        if (user.Id > 0) await EnsureCanManageUserAsync(user.Id);

        var isNew = user.Id == 0;
        UserEntity? old = null;
        if (isNew)
        {
            var pwd = string.IsNullOrEmpty(newPassword) ? throw new ScadaSecurityException("新用户必须设置密码") : newPassword;
            var err = options.ValidatePassword(pwd);
            if (err != null) throw new ScadaSecurityException(err);
            user.PasswordHash = PasswordHasher.Hash(pwd);
            user.PasswordChangedAt = DateTime.Now;
            user.Id = await db.Insertable(user).ExecuteReturnBigIdentityAsync();
        }
        else
        {
            old = await db.Queryable<UserEntity>().InSingleAsync(user.Id);
            if (old.IsBuiltIn && !user.IsEnabled) throw new ScadaSecurityException("内置用户不能禁用");
            user.PasswordHash = old.PasswordHash;
            user.IsBuiltIn = old.IsBuiltIn;
            user.UpdatedAt = DateTime.Now;
            await db.Updateable(user).IgnoreColumns(u => new { u.PasswordHash, u.FailedCount, u.LockedUntil, u.LastLoginAt, u.PasswordChangedAt, u.CreatedAt }).ExecuteCommandAsync();
            if (!string.IsNullOrEmpty(newPassword)) await ResetPasswordAsync(user.Id, newPassword);
        }

        await db.Deleteable<UserRoleEntity>().Where(r => r.UserId == user.Id).ExecuteCommandAsync();
        if (roleIds.Count > 0)
            await db.Insertable(roleIds.Distinct().Select(r => new UserRoleEntity { UserId = user.Id, RoleId = r }).ToList()).ExecuteCommandAsync();

        await audit.LogAsync(AuditActions.UserChange, $"user:{user.UserName}",
            old == null ? null : new { old.DisplayName, old.CardNo, old.IsEnabled },
            new { user.DisplayName, user.CardNo, user.IsEnabled, Roles = roleIds });
        return user;
    }

    /// <summary>删除用户。</summary>
    public async Task DeleteUserAsync(long userId)
    {
        permissions.Demand(Permissions.User.Manage);
        var user = await db.Queryable<UserEntity>().InSingleAsync(userId) ?? throw new ScadaSecurityException("用户不存在");
        if (user.IsBuiltIn) throw new ScadaSecurityException("内置用户不能删除");
        if (user.Id == currentUser.User?.Id) throw new ScadaSecurityException("不能删除当前登录用户");
        await EnsureCanManageUserAsync(userId);
        await db.Deleteable<UserRoleEntity>().Where(r => r.UserId == userId).ExecuteCommandAsync();
        await db.Deleteable<UserEntity>().In(userId).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.UserChange, $"user:{user.UserName}", "存在", "已删除");
    }

    /// <summary>重置枚举密码。</summary>
    public async Task ResetPasswordAsync(long userId, string newPassword)
    {
        permissions.Demand(Permissions.User.ResetPassword);
        await EnsureCanManageUserAsync(userId);
        var err = options.ValidatePassword(newPassword);
        if (err != null) throw new ScadaSecurityException(err);
        var user = await db.Queryable<UserEntity>().InSingleAsync(userId);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.PasswordChangedAt = options.PasswordExpireDays > 0 ? DateTime.Now.AddDays(-options.PasswordExpireDays - 1) : DateTime.Now;
        await db.Updateable(user).UpdateColumns(u => new { u.PasswordHash, u.PasswordChangedAt }).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.UserChange, $"user:{user.UserName}", newValue: "重置密码");
    }

    /// <summary>解锁。</summary>
    public async Task UnlockAsync(long userId)
    {
        permissions.Demand(Permissions.User.Manage);
        await db.Updateable<UserEntity>().SetColumns(u => new UserEntity { FailedCount = 0, LockedUntil = null }).Where(u => u.Id == userId).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.UserChange, $"user:{userId}", newValue: "解锁");
    }

    /// <summary>保存角色。</summary>
    public async Task<RoleEntity> SaveRoleAsync(RoleEntity role)
    {
        permissions.Demand(Permissions.User.ManageRole);
        if (string.IsNullOrWhiteSpace(role.Code)) throw new ScadaSecurityException("角色编码不能为空");
        if (await db.Queryable<RoleEntity>().AnyAsync(r => r.Code == role.Code && r.Id != role.Id))
            throw new ScadaSecurityException($"角色编码 {role.Code} 已存在");
        if (role.Level >= currentUser.MaxRoleLevel && !IsSuper())
            throw new ScadaSecurityException("不能创建或修改等级不低于自身的角色");

        if (role.Id == 0)
        {
            role.Id = await db.Insertable(role).ExecuteReturnBigIdentityAsync();
        }
        else
        {
            var old = await db.Queryable<RoleEntity>().InSingleAsync(role.Id);
            if (old.IsBuiltIn && old.Code != role.Code) throw new ScadaSecurityException("内置角色编码不可修改");
            role.IsBuiltIn = old.IsBuiltIn;
            role.UpdatedAt = DateTime.Now;
            await db.Updateable(role).IgnoreColumns(r => r.CreatedAt).ExecuteCommandAsync();
        }
        await audit.LogAsync(AuditActions.RoleChange, $"role:{role.Code}", newValue: new { role.Name, role.Level });
        return role;
    }

    /// <summary>删除角色。</summary>
    public async Task DeleteRoleAsync(long roleId)
    {
        permissions.Demand(Permissions.User.ManageRole);
        var role = await db.Queryable<RoleEntity>().InSingleAsync(roleId) ?? throw new ScadaSecurityException("角色不存在");
        if (role.IsBuiltIn) throw new ScadaSecurityException("内置角色不能删除");
        await db.Deleteable<RolePermissionEntity>().Where(r => r.RoleId == roleId).ExecuteCommandAsync();
        await db.Deleteable<RoleDataScopeEntity>().Where(r => r.RoleId == roleId).ExecuteCommandAsync();
        await db.Deleteable<UserRoleEntity>().Where(r => r.RoleId == roleId).ExecuteCommandAsync();
        await db.Deleteable<RoleEntity>().In(roleId).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.RoleChange, $"role:{role.Code}", "存在", "已删除");
    }

    /// <summary>设置角色权限。</summary>
    public async Task SetRolePermissionsAsync(long roleId, IReadOnlyCollection<string> permissionCodes)
    {
        permissions.Demand(Permissions.User.ManageRole);
        var role = await db.Queryable<RoleEntity>().InSingleAsync(roleId);
        if (role.Code == BuiltInRoles.SuperAdmin) throw new ScadaSecurityException("超级管理员拥有全部权限，无需授权");
        if (!IsSuper())
        {
            var notOwned = permissionCodes.Where(p => !permissions.Has(p)).ToList();
            if (notOwned.Count > 0) throw new ScadaSecurityException($"不能授予自身不具备的权限：{string.Join(",", notOwned)}");
        }
        var old = await GetRolePermissionsAsync(roleId);
        await db.Deleteable<RolePermissionEntity>().Where(r => r.RoleId == roleId).ExecuteCommandAsync();
        if (permissionCodes.Count > 0)
            await db.Insertable(permissionCodes.Distinct().Select(c => new RolePermissionEntity { RoleId = roleId, PermissionCode = c }).ToList()).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.RoleChange, $"role:{role.Code}:permissions", old, permissionCodes);
    }

    /// <summary>设置角色数据范围。</summary>
    public async Task SetRoleDataScopesAsync(long roleId, IReadOnlyCollection<DataScope> scopes)
    {
        permissions.Demand(Permissions.User.ManageRole);
        await db.Deleteable<RoleDataScopeEntity>().Where(r => r.RoleId == roleId).ExecuteCommandAsync();
        if (scopes.Count > 0)
            await db.Insertable(scopes.Select(s => new RoleDataScopeEntity { RoleId = roleId, ScopeType = s.ScopeType, ScopeValue = s.ScopeValue }).ToList()).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.RoleChange, $"role:{roleId}:scopes", newValue: scopes);
    }

    /// <summary>判断类型超级。</summary>
    private bool IsSuper() => currentUser.Roles.Any(r => r.Code == BuiltInRoles.SuperAdmin);

    /// <summary>确保能否分配角色。</summary>
    private async Task EnsureCanAssignRolesAsync(IReadOnlyList<long> roleIds)
    {
        if (IsSuper() || roleIds.Count == 0) return;
        var maxLevel = await db.Queryable<RoleEntity>().Where(r => roleIds.Contains(r.Id)).MaxAsync(r => r.Level);
        if (maxLevel >= currentUser.MaxRoleLevel) throw new ScadaSecurityException("不能分配等级不低于自身的角色");
    }

    /// <summary>确保能否管理用户。</summary>
    private async Task EnsureCanManageUserAsync(long userId)
    {
        if (IsSuper() || userId == currentUser.User?.Id) return;
        var roleIds = await db.Queryable<UserRoleEntity>().Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync();
        if (roleIds.Count == 0) return;
        var maxLevel = await db.Queryable<RoleEntity>().Where(r => roleIds.Contains(r.Id)).MaxAsync(r => r.Level);
        if (maxLevel >= currentUser.MaxRoleLevel) throw new ScadaSecurityException("不能管理等级不低于自身的用户");
    }
}
