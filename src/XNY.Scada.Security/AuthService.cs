using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

/// <summary>登录结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="Message">消息。</param>
/// <param name="MustChangePassword">必须更改密码。</param>
public sealed record LoginResult(bool Success, string Message, bool MustChangePassword = false)
{
    /// <summary>失败。</summary>
    public static LoginResult Fail(string message) => new(false, message);
}

/// <summary>I认证服务。</summary>
public interface IAuthService
{
    /// <summary>登录。</summary>
    Task<LoginResult> LoginAsync(string userName, string password);

    /// <summary>登录按卡。</summary>
    Task<LoginResult> LoginByCardAsync(string cardNo);

    /// <summary>登出。</summary>
    Task LogoutAsync(string reason = "");

    /// <summary>更改密码。</summary>
    Task<string?> ChangePasswordAsync(string oldPassword, string newPassword);

    /// <summary>电子签名：校验签名人身份与权限（不改变当前登录用户），返回签名人用户名</summary>
    Task<SignatureResult> VerifySignatureAsync(string userName, string password, string requiredPermission);
}

/// <summary>签名结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="Message">消息。</param>
/// <param name="Signer">电子签名确认人。</param>
public sealed record SignatureResult(bool Success, string Message, string? Signer);

/// <summary>认证服务。</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="session">会话。</param>
/// <param name="audit">审计。</param>
/// <param name="options">配置项。</param>
/// <param name="logger">日志记录器。</param>
public sealed class AuthService(
    ISqlSugarClient db,
    UserSession session,
    IAuditService audit,
    SecurityOptions options,
    ILogger<AuthService> logger) : IAuthService
{
    /// <summary>登录。</summary>
    public async Task<LoginResult> LoginAsync(string userName, string password)
    {
        var user = await db.Queryable<UserEntity>().FirstAsync(u => u.UserName == userName);
        var check = await CheckCredentialAsync(user, password);
        if (!check.Success)
        {
            await audit.LogAsync(AuditActions.LoginFailed, userName, newValue: check.Message);
            return check;
        }
        return await SignInAsync(user!, "密码");
    }

    /// <summary>登录按卡。</summary>
    public async Task<LoginResult> LoginByCardAsync(string cardNo)
    {
        cardNo = cardNo.Trim();
        if (cardNo.Length == 0) return LoginResult.Fail("卡号为空");
        var user = await db.Queryable<UserEntity>().FirstAsync(u => u.CardNo == cardNo);
        if (user == null)
        {
            await audit.LogAsync(AuditActions.LoginFailed, $"card:{cardNo}", newValue: "未绑定的卡号");
            return LoginResult.Fail("卡号未绑定用户");
        }
        if (!user.IsEnabled) return LoginResult.Fail("用户已禁用");
        if (user.LockedUntil > DateTime.Now) return LoginResult.Fail($"用户已锁定至 {user.LockedUntil:HH:mm:ss}");
        return await SignInAsync(user, "刷卡");
    }

    /// <summary>检查凭据。</summary>
    private async Task<LoginResult> CheckCredentialAsync(UserEntity? user, string password)
    {
        if (user == null) return LoginResult.Fail("用户名或密码错误");
        if (!user.IsEnabled) return LoginResult.Fail("用户已禁用");
        if (user.LockedUntil > DateTime.Now) return LoginResult.Fail($"连续登录失败，已锁定至 {user.LockedUntil:HH:mm:ss}");

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedCount++;
            if (options.MaxFailedAttempts > 0 && user.FailedCount >= options.MaxFailedAttempts)
            {
                user.LockedUntil = DateTime.Now.AddMinutes(options.LockMinutes);
                user.FailedCount = 0;
                logger.LogWarning("用户 {User} 连续登录失败，锁定 {Minutes} 分钟", user.UserName, options.LockMinutes);
            }
            await db.Updateable(user).UpdateColumns(u => new { u.FailedCount, u.LockedUntil }).ExecuteCommandAsync();
            return LoginResult.Fail("用户名或密码错误");
        }
        return new LoginResult(true, "");
    }

    /// <summary>签名在。</summary>
    private async Task<LoginResult> SignInAsync(UserEntity user, string method)
    {
        var roleIds = await db.Queryable<UserRoleEntity>().Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync();
        var roles = await db.Queryable<RoleEntity>().Where(r => roleIds.Contains(r.Id)).ToListAsync();
        var permissions = await db.Queryable<RolePermissionEntity>().Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionCode).ToListAsync();
        var scopes = await db.Queryable<RoleDataScopeEntity>().Where(s => roleIds.Contains(s.RoleId)).ToListAsync();

        user.FailedCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.Now;
        await db.Updateable(user).UpdateColumns(u => new { u.FailedCount, u.LockedUntil, u.LastLoginAt }).ExecuteCommandAsync();

        session.SignIn(user, roles, permissions.Distinct(), scopes.Select(s => new DataScope(s.ScopeType, s.ScopeValue)).ToList());
        await audit.LogAsync(AuditActions.Login, user.UserName, newValue: method);
        logger.LogInformation("用户 {User} 通过{Method}登录", user.UserName, method);

        var expired = options.PasswordExpireDays > 0 && user.PasswordChangedAt.AddDays(options.PasswordExpireDays) < DateTime.Now;
        return new LoginResult(true, expired ? "密码已过期，请修改密码" : "登录成功", expired);
    }

    /// <summary>登出。</summary>
    public async Task LogoutAsync(string reason = "")
    {
        if (!session.IsAuthenticated) return;
        await audit.LogAsync(AuditActions.Logout, session.UserName, newValue: reason);
        session.SignOut();
    }

    /// <summary>更改密码。</summary>
    public async Task<string?> ChangePasswordAsync(string oldPassword, string newPassword)
    {
        var user = session.User;
        if (user == null) return "未登录";
        var fresh = await db.Queryable<UserEntity>().InSingleAsync(user.Id);
        if (!PasswordHasher.Verify(oldPassword, fresh.PasswordHash)) return "原密码错误";
        var error = options.ValidatePassword(newPassword);
        if (error != null) return error;
        fresh.PasswordHash = PasswordHasher.Hash(newPassword);
        fresh.PasswordChangedAt = DateTime.Now;
        await db.Updateable(fresh).UpdateColumns(u => new { u.PasswordHash, u.PasswordChangedAt }).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.ChangePassword, fresh.UserName);
        return null;
    }

    /// <summary>校验签名。</summary>
    public async Task<SignatureResult> VerifySignatureAsync(string userName, string password, string requiredPermission)
    {
        var user = await db.Queryable<UserEntity>().FirstAsync(u => u.UserName == userName);
        var check = await CheckCredentialAsync(user, password);
        if (!check.Success) return new SignatureResult(false, check.Message, null);

        var roleIds = await db.Queryable<UserRoleEntity>().Where(ur => ur.UserId == user!.Id).Select(ur => ur.RoleId).ToListAsync();
        var isSuper = await db.Queryable<RoleEntity>().AnyAsync(r => roleIds.Contains(r.Id) && r.Code == BuiltInRoles.SuperAdmin);
        var has = isSuper || await db.Queryable<RolePermissionEntity>()
            .AnyAsync(rp => roleIds.Contains(rp.RoleId) && rp.PermissionCode == requiredPermission);
        return has
            ? new SignatureResult(true, "签名成功", user!.UserName)
            : new SignatureResult(false, $"签名人没有权限：{requiredPermission}", null);
    }
}
