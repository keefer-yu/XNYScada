using SqlSugar;

namespace XNY.Scada.Domain.Entities;

/// <summary>当前用户实体，对应表 sys_user。</summary>
[SugarTable("sys_user")]
public class UserEntity : EntityBase
{
    /// <summary>用户名。</summary>
    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";

    /// <summary>显示名。</summary>
    [SugarColumn(Length = 64)]
    public string DisplayName { get; set; } = "";

    /// <summary>密码哈希。</summary>
    [SugarColumn(Length = 256)]
    public string PasswordHash { get; set; } = "";

    /// <summary>卡号。</summary>
    [SugarColumn(Length = 64)]
    public string? CardNo { get; set; }

    /// <summary>是否当前用户是否启用。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>是否为内置数据。</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>连续失败次数。</summary>
    public int FailedCount { get; set; }

    /// <summary>锁定截止时间。</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>密码修改时间。</summary>
    public DateTime PasswordChangedAt { get; set; } = DateTime.Now;

    /// <summary>最近登录时间。</summary>
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>角色实体，对应表 sys_role。</summary>
[SugarTable("sys_role")]
public class RoleEntity : EntityBase
{
    /// <summary>角色编码。</summary>
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    /// <summary>角色名称。</summary>
    [SugarColumn(Length = 64)]
    public string Name { get; set; } = "";

    /// <summary>角色等级，数值越大权限越高，用于限制低等级用户管理高等级用户</summary>
    public int Level { get; set; }

    /// <summary>是否为内置数据。</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>角色说明。</summary>
    public string? Description { get; set; }
}

/// <summary>权限实体，对应表 sys_permission。</summary>
[SugarTable("sys_permission")]
public class PermissionEntity : EntityBase
{
    /// <summary>权限编码。</summary>
    [SugarColumn(Length = 128)]
    public string Code { get; set; } = "";

    /// <summary>权限名称。</summary>
    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    /// <summary>分类。</summary>
    [SugarColumn(Length = 64)]
    public string Category { get; set; } = "";

    /// <summary>权限种类。</summary>
    public PermissionKind Kind { get; set; }

    /// <summary>排序号。</summary>
    public int Sort { get; set; }
}

/// <summary>用户角色实体，对应表 sys_user_role。</summary>
[SugarTable("sys_user_role")]
public class UserRoleEntity : EntityBase
{
    /// <summary>用户标识。</summary>
    public long UserId { get; set; }

    /// <summary>角色标识。</summary>
    public long RoleId { get; set; }
}

/// <summary>角色权限实体，对应表 sys_role_permission。</summary>
[SugarTable("sys_role_permission")]
public class RolePermissionEntity : EntityBase
{
    /// <summary>角色标识。</summary>
    public long RoleId { get; set; }

    /// <summary>权限编码。</summary>
    [SugarColumn(Length = 128)]
    public string PermissionCode { get; set; } = "";
}

/// <summary>数据权限：限制角色可访问的设备/工位，未配置表示全部</summary>
[SugarTable("sys_role_data_scope")]
public class RoleDataScopeEntity : EntityBase
{
    /// <summary>角色标识。</summary>
    public long RoleId { get; set; }

    /// <summary>Device / Station</summary>
    [SugarColumn(Length = 32)]
    public string ScopeType { get; set; } = "";

    /// <summary>数据范围值。</summary>
    [SugarColumn(Length = 128)]
    public string ScopeValue { get; set; } = "";
}

/// <summary>审计日志实体，对应表 sys_audit_log。</summary>
[SugarTable("sys_audit_log")]
public class AuditLogEntity : EntityBase
{
    /// <summary>用户名。</summary>
    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";

    /// <summary>动作。</summary>
    [SugarColumn(Length = 64)]
    public string Action { get; set; } = "";

    /// <summary>操作对象。</summary>
    [SugarColumn(Length = 256)]
    public string Target { get; set; } = "";

    /// <summary>修改前的值。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? OldValue { get; set; }

    /// <summary>修改后的值。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? NewValue { get; set; }

    /// <summary>电子签名确认人</summary>
    [SugarColumn(Length = 64)]
    public string? Signer { get; set; }

    /// <summary>原因。</summary>
    [SugarColumn(Length = 256)]
    public string? Reason { get; set; }

    /// <summary>机器名。</summary>
    [SugarColumn(Length = 64)]
    public string? Machine { get; set; }
}

/// <summary>系统参数实体，对应表 sys_param。</summary>
[SugarTable("sys_param")]
public class SystemParamEntity : EntityBase
{
    /// <summary>系统参数键。</summary>
    [SugarColumn(Length = 128)]
    public string Key { get; set; } = "";

    /// <summary>系统参数值。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Value { get; set; } = "";

    /// <summary>分组。</summary>
    [SugarColumn(Length = 64)]
    public string Group { get; set; } = "";

    /// <summary>系统参数说明。</summary>
    public string? Description { get; set; }
}
