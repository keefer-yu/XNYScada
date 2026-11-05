using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

/// <summary>审计查询。</summary>
/// <param name="From">起点节点。</param>
/// <param name="To">目标节点。</param>
/// <param name="UserName">用户名。</param>
/// <param name="Action">动作。</param>
/// <param name="Keyword">关键字。</param>
/// <param name="Limit">限制。</param>
public sealed record AuditQuery(DateTime From, DateTime To, string? UserName = null, string? Action = null, string? Keyword = null, int Limit = 1000);

/// <summary>I审计服务。</summary>
public interface IAuditService
{
    /// <summary>记录日志。</summary>
    Task LogAsync(string action, string target, object? oldValue = null, object? newValue = null, string? signer = null, string? reason = null);

    /// <summary>查询。</summary>
    Task<List<AuditLogEntity>> QueryAsync(AuditQuery query);
}

/// <summary>审计服务。</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="currentUser">当前用户。</param>
public sealed class AuditService(ISqlSugarClient db, ICurrentUser currentUser) : IAuditService
{
    /// <summary>记录日志。</summary>
    public Task LogAsync(string action, string target, object? oldValue = null, object? newValue = null, string? signer = null, string? reason = null)
    {
        var log = new AuditLogEntity
        {
            UserName = currentUser.UserName,
            Action = action,
            Target = target,
            OldValue = Format(oldValue),
            NewValue = Format(newValue),
            Signer = signer,
            Reason = reason,
            Machine = Environment.MachineName
        };
        return db.Insertable(log).ExecuteCommandAsync();
    }

    /// <summary>查询。</summary>
    public Task<List<AuditLogEntity>> QueryAsync(AuditQuery q) =>
        db.Queryable<AuditLogEntity>()
            .Where(a => a.CreatedAt >= q.From && a.CreatedAt <= q.To)
            .WhereIF(!string.IsNullOrWhiteSpace(q.UserName), a => a.UserName == q.UserName)
            .WhereIF(!string.IsNullOrWhiteSpace(q.Action), a => a.Action == q.Action)
            .WhereIF(!string.IsNullOrWhiteSpace(q.Keyword), a => a.Target.Contains(q.Keyword!) || a.NewValue!.Contains(q.Keyword!))
            .OrderBy(a => a.Id, OrderByType.Desc)
            .Take(q.Limit)
            .ToListAsync();

    /// <summary>格式化。</summary>
    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        _ when value.GetType().IsPrimitive => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        _ => Json.Serialize(value)
    };
}

/// <summary>审计动作。</summary>
public static class AuditActions
{
    /// <summary>登录。</summary>
    public const string Login = "Login";
    /// <summary>登录失败。</summary>
    public const string LoginFailed = "LoginFailed";
    /// <summary>退出登录。</summary>
    public const string Logout = "Logout";
    /// <summary>更改密码。</summary>
    public const string ChangePassword = "ChangePassword";
    /// <summary>参数更改。</summary>
    public const string ParamChange = "ParamChange";
    /// <summary>设备更改。</summary>
    public const string DeviceChange = "DeviceChange";
    /// <summary>手动写。</summary>
    public const string ManualWrite = "ManualWrite";
    /// <summary>流保存。</summary>
    public const string FlowSave = "FlowSave";
    /// <summary>流发布。</summary>
    public const string FlowPublish = "FlowPublish";
    /// <summary>配方更改。</summary>
    public const string RecipeChange = "RecipeChange";
    /// <summary>配方审批。</summary>
    public const string RecipeApprove = "RecipeApprove";
    /// <summary>配方切换。</summary>
    public const string RecipeSwitch = "RecipeSwitch";
    /// <summary>用户更改。</summary>
    public const string UserChange = "UserChange";
    /// <summary>角色更改。</summary>
    public const string RoleChange = "RoleChange";
    /// <summary>MES更改。</summary>
    public const string MesChange = "MesChange";
    /// <summary>模板导入。</summary>
    public const string TemplateImport = "TemplateImport";
    /// <summary>报警确认。</summary>
    public const string AlarmAck = "AlarmAck";
    /// <summary>生产。</summary>
    public const string Production = "Production";
}
