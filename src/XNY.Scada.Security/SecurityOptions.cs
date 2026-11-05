namespace XNY.Scada.Security;

/// <summary>安全的配置项。</summary>
public sealed class SecurityOptions
{
    /// <summary>配置节名称。</summary>
    public const string Section = "Security";

    /// <summary>最短密码长度。</summary>
    public int MinPasswordLength { get; set; } = 6;

    /// <summary>是否密码是否必须包含数字。</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>是否密码是否必须包含字母。</summary>
    public bool RequireLetter { get; set; } = true;

    /// <summary>密码有效期（天），0 表示永不过期</summary>
    public int PasswordExpireDays { get; set; }

    /// <summary>锁定前允许的连续失败次数。</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>锁定时长（分钟）。</summary>
    public int LockMinutes { get; set; } = 10;

    /// <summary>无操作自动登出（分钟），0 表示不自动登出</summary>
    public int AutoLogoutMinutes { get; set; } = 15;

    /// <summary>初始超级管理员密码</summary>
    public string DefaultAdminPassword { get; set; } = "Admin@123";

    /// <summary>刷卡器串口，为空表示使用 HID（键盘模拟）读卡器</summary>
    public string? CardReaderPort { get; set; }

    /// <summary>读卡器波特率。</summary>
    public int CardReaderBaudRate { get; set; } = 9600;

    /// <summary>校验密码。</summary>
    public string? ValidatePassword(string password)
    {
        if (password.Length < MinPasswordLength) return $"密码长度不能少于 {MinPasswordLength} 位";
        if (RequireDigit && !password.Any(char.IsDigit)) return "密码必须包含数字";
        if (RequireLetter && !password.Any(char.IsLetter)) return "密码必须包含字母";
        return null;
    }
}

/// <summary>SCADA安全异常。</summary>
/// <param name="message">消息。</param>
public class ScadaSecurityException(string message) : Exception(message);

/// <summary>权限已拒绝异常。</summary>
/// <param name="permission">权限。</param>
public sealed class PermissionDeniedException(string permission)
    : ScadaSecurityException($"当前用户没有权限：{permission}")
{
    /// <summary>权限。</summary>
    public string Permission { get; } = permission;
}
