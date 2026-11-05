using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

public sealed record AuditQuery(DateTime From, DateTime To, string? UserName = null, string? Action = null, string? Keyword = null, int Limit = 1000);

public interface IAuditService
{
    Task LogAsync(string action, string target, object? oldValue = null, object? newValue = null, string? signer = null, string? reason = null);

    Task<List<AuditLogEntity>> QueryAsync(AuditQuery query);
}

public sealed class AuditService(ISqlSugarClient db, ICurrentUser currentUser) : IAuditService
{
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

    public Task<List<AuditLogEntity>> QueryAsync(AuditQuery q) =>
        db.Queryable<AuditLogEntity>()
            .Where(a => a.CreatedAt >= q.From && a.CreatedAt <= q.To)
            .WhereIF(!string.IsNullOrWhiteSpace(q.UserName), a => a.UserName == q.UserName)
            .WhereIF(!string.IsNullOrWhiteSpace(q.Action), a => a.Action == q.Action)
            .WhereIF(!string.IsNullOrWhiteSpace(q.Keyword), a => a.Target.Contains(q.Keyword!) || a.NewValue!.Contains(q.Keyword!))
            .OrderBy(a => a.Id, OrderByType.Desc)
            .Take(q.Limit)
            .ToListAsync();

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        _ when value.GetType().IsPrimitive => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        _ => Json.Serialize(value)
    };
}

public static class AuditActions
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string ChangePassword = "ChangePassword";
    public const string ParamChange = "ParamChange";
    public const string DeviceChange = "DeviceChange";
    public const string ManualWrite = "ManualWrite";
    public const string FlowSave = "FlowSave";
    public const string FlowPublish = "FlowPublish";
    public const string RecipeChange = "RecipeChange";
    public const string RecipeApprove = "RecipeApprove";
    public const string RecipeSwitch = "RecipeSwitch";
    public const string UserChange = "UserChange";
    public const string RoleChange = "RoleChange";
    public const string MesChange = "MesChange";
    public const string TemplateImport = "TemplateImport";
    public const string AlarmAck = "AlarmAck";
    public const string Production = "Production";
}
