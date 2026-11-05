namespace XNY.Scada.Security;

public sealed class SecurityOptions
{
    public const string Section = "Security";

    public int MinPasswordLength { get; set; } = 6;

    public bool RequireDigit { get; set; } = true;

    public bool RequireLetter { get; set; } = true;

    /// <summary>密码有效期（天），0 表示永不过期</summary>
    public int PasswordExpireDays { get; set; }

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockMinutes { get; set; } = 10;

    /// <summary>无操作自动登出（分钟），0 表示不自动登出</summary>
    public int AutoLogoutMinutes { get; set; } = 15;

    /// <summary>初始超级管理员密码</summary>
    public string DefaultAdminPassword { get; set; } = "Admin@123";

    /// <summary>刷卡器串口，为空表示使用 HID（键盘模拟）读卡器</summary>
    public string? CardReaderPort { get; set; }

    public int CardReaderBaudRate { get; set; } = 9600;

    public string? ValidatePassword(string password)
    {
        if (password.Length < MinPasswordLength) return $"密码长度不能少于 {MinPasswordLength} 位";
        if (RequireDigit && !password.Any(char.IsDigit)) return "密码必须包含数字";
        if (RequireLetter && !password.Any(char.IsLetter)) return "密码必须包含字母";
        return null;
    }
}

public class ScadaSecurityException(string message) : Exception(message);

public sealed class PermissionDeniedException(string permission)
    : ScadaSecurityException($"当前用户没有权限：{permission}")
{
    public string Permission { get; } = permission;
}
