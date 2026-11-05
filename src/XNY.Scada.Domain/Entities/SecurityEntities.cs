using SqlSugar;

namespace XNY.Scada.Domain.Entities;

[SugarTable("sys_user")]
public class UserEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string DisplayName { get; set; } = "";

    [SugarColumn(Length = 256)]
    public string PasswordHash { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string? CardNo { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsBuiltIn { get; set; }

    public int FailedCount { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime PasswordChangedAt { get; set; } = DateTime.Now;

    public DateTime? LastLoginAt { get; set; }
}

[SugarTable("sys_role")]
public class RoleEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    /// <summary>角色等级，数值越大权限越高，用于限制低等级用户管理高等级用户</summary>
    public int Level { get; set; }

    public bool IsBuiltIn { get; set; }

    public string? Description { get; set; }
}

[SugarTable("sys_permission")]
public class PermissionEntity : EntityBase
{
    [SugarColumn(Length = 128)]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Category { get; set; } = "";

    public PermissionKind Kind { get; set; }

    public int Sort { get; set; }
}

[SugarTable("sys_user_role")]
public class UserRoleEntity : EntityBase
{
    public long UserId { get; set; }

    public long RoleId { get; set; }
}

[SugarTable("sys_role_permission")]
public class RolePermissionEntity : EntityBase
{
    public long RoleId { get; set; }

    [SugarColumn(Length = 128)]
    public string PermissionCode { get; set; } = "";
}

/// <summary>数据权限：限制角色可访问的设备/工位，未配置表示全部</summary>
[SugarTable("sys_role_data_scope")]
public class RoleDataScopeEntity : EntityBase
{
    public long RoleId { get; set; }

    /// <summary>Device / Station</summary>
    [SugarColumn(Length = 32)]
    public string ScopeType { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string ScopeValue { get; set; } = "";
}

[SugarTable("sys_audit_log")]
public class AuditLogEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Action { get; set; } = "";

    [SugarColumn(Length = 256)]
    public string Target { get; set; } = "";

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? OldValue { get; set; }

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? NewValue { get; set; }

    /// <summary>电子签名确认人</summary>
    [SugarColumn(Length = 64)]
    public string? Signer { get; set; }

    [SugarColumn(Length = 256)]
    public string? Reason { get; set; }

    [SugarColumn(Length = 64)]
    public string? Machine { get; set; }
}

[SugarTable("sys_param")]
public class SystemParamEntity : EntityBase
{
    [SugarColumn(Length = 128)]
    public string Key { get; set; } = "";

    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Value { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Group { get; set; } = "";

    public string? Description { get; set; }
}
