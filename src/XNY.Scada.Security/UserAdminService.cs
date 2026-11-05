using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

public sealed record UserInfo(UserEntity User, IReadOnlyList<long> RoleIds);

public interface IUserAdminService
{
    Task<List<UserInfo>> GetUsersAsync();

    Task<List<RoleEntity>> GetRolesAsync();

    Task<List<PermissionEntity>> GetPermissionsAsync();

    Task<List<string>> GetRolePermissionsAsync(long roleId);

    Task<List<RoleDataScopeEntity>> GetRoleDataScopesAsync(long roleId);

    Task<UserEntity> SaveUserAsync(UserEntity user, IReadOnlyList<long> roleIds, string? newPassword);

    Task DeleteUserAsync(long userId);

    Task ResetPasswordAsync(long userId, string newPassword);

    Task UnlockAsync(long userId);

    Task<RoleEntity> SaveRoleAsync(RoleEntity role);

    Task DeleteRoleAsync(long roleId);

    Task SetRolePermissionsAsync(long roleId, IReadOnlyCollection<string> permissionCodes);

    Task SetRoleDataScopesAsync(long roleId, IReadOnlyCollection<DataScope> scopes);
}

public sealed class UserAdminService(
    ISqlSugarClient db,
    IPermissionService permissions,
    ICurrentUser currentUser,
    IAuditService audit,
    SecurityOptions options) : IUserAdminService
{
    public async Task<List<UserInfo>> GetUsersAsync()
    {
        var users = await db.Queryable<UserEntity>().OrderBy(u => u.Id).ToListAsync();
        var links = await db.Queryable<UserRoleEntity>().ToListAsync();
        return users.Select(u => new UserInfo(u, links.Where(l => l.UserId == u.Id).Select(l => l.RoleId).ToList())).ToList();
    }

    public Task<List<RoleEntity>> GetRolesAsync() => db.Queryable<RoleEntity>().OrderBy(r => r.Level).ToListAsync();

    public Task<List<PermissionEntity>> GetPermissionsAsync() => db.Queryable<PermissionEntity>().OrderBy(p => p.Sort).ToListAsync();

    public Task<List<string>> GetRolePermissionsAsync(long roleId) =>
        db.Queryable<RolePermissionEntity>().Where(r => r.RoleId == roleId).Select(r => r.PermissionCode).ToListAsync();

    public Task<List<RoleDataScopeEntity>> GetRoleDataScopesAsync(long roleId) =>
        db.Queryable<RoleDataScopeEntity>().Where(r => r.RoleId == roleId).ToListAsync();

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

    public async Task UnlockAsync(long userId)
    {
        permissions.Demand(Permissions.User.Manage);
        await db.Updateable<UserEntity>().SetColumns(u => new UserEntity { FailedCount = 0, LockedUntil = null }).Where(u => u.Id == userId).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.UserChange, $"user:{userId}", newValue: "解锁");
    }

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

    public async Task SetRoleDataScopesAsync(long roleId, IReadOnlyCollection<DataScope> scopes)
    {
        permissions.Demand(Permissions.User.ManageRole);
        await db.Deleteable<RoleDataScopeEntity>().Where(r => r.RoleId == roleId).ExecuteCommandAsync();
        if (scopes.Count > 0)
            await db.Insertable(scopes.Select(s => new RoleDataScopeEntity { RoleId = roleId, ScopeType = s.ScopeType, ScopeValue = s.ScopeValue }).ToList()).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.RoleChange, $"role:{roleId}:scopes", newValue: scopes);
    }

    private bool IsSuper() => currentUser.Roles.Any(r => r.Code == BuiltInRoles.SuperAdmin);

    private async Task EnsureCanAssignRolesAsync(IReadOnlyList<long> roleIds)
    {
        if (IsSuper() || roleIds.Count == 0) return;
        var maxLevel = await db.Queryable<RoleEntity>().Where(r => roleIds.Contains(r.Id)).MaxAsync(r => r.Level);
        if (maxLevel >= currentUser.MaxRoleLevel) throw new ScadaSecurityException("不能分配等级不低于自身的角色");
    }

    private async Task EnsureCanManageUserAsync(long userId)
    {
        if (IsSuper() || userId == currentUser.User?.Id) return;
        var roleIds = await db.Queryable<UserRoleEntity>().Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync();
        if (roleIds.Count == 0) return;
        var maxLevel = await db.Queryable<RoleEntity>().Where(r => roleIds.Contains(r.Id)).MaxAsync(r => r.Level);
        if (maxLevel >= currentUser.MaxRoleLevel) throw new ScadaSecurityException("不能管理等级不低于自身的用户");
    }
}
