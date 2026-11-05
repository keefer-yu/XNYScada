using System.Reflection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Security;

/// <summary>同步权限点到数据库，并初始化内置角色与超级管理员</summary>
public sealed class SecuritySeeder(ISqlSugarClient db, SecurityOptions options, ILogger<SecuritySeeder> logger)
{
    private static readonly (string Code, string Name, int Level, string[] Permissions)[] Roles =
    [
        (BuiltInRoles.Operator, "操作员", 10,
        [
            Permissions.Menu.Dashboard, Permissions.Menu.Alarm, Permissions.Menu.Production,
            Permissions.Alarm.Acknowledge, Permissions.Production.Start, Permissions.Production.ManualTrigger
        ]),
        (BuiltInRoles.Technician, "技术员", 20,
        [
            Permissions.Menu.Dashboard, Permissions.Menu.Alarm, Permissions.Menu.Production, Permissions.Menu.Diagnostics,
            Permissions.Menu.FlowMonitor, Permissions.Menu.Recipe,
            Permissions.Alarm.Acknowledge, Permissions.Production.Start, Permissions.Production.ManualTrigger,
            Permissions.Production.ResetCounter, Permissions.Production.TrayManual, Permissions.Production.Export,
            Permissions.Recipe.Switch, Permissions.Device.ManualWrite
        ]),
        (BuiltInRoles.Engineer, "工程师", 30,
        [
            Permissions.Menu.Dashboard, Permissions.Menu.Alarm, Permissions.Menu.Production, Permissions.Menu.Diagnostics,
            Permissions.Menu.FlowMonitor, Permissions.Menu.Recipe, Permissions.Menu.Device, Permissions.Menu.FlowDesigner,
            Permissions.Menu.Mes, Permissions.Menu.System, Permissions.Menu.Audit,
            Permissions.Alarm.Acknowledge, Permissions.Alarm.Configure,
            Permissions.Production.Start, Permissions.Production.ManualTrigger, Permissions.Production.ResetCounter,
            Permissions.Production.TrayManual, Permissions.Production.Export,
            Permissions.Device.Edit, Permissions.Device.ManualWrite, Permissions.Device.Reload,
            Permissions.Flow.Edit, Permissions.Flow.Debug, Permissions.Flow.Control,
            Permissions.Recipe.Edit, Permissions.Recipe.Switch, Permissions.Mes.Edit, Permissions.Mes.Test
        ]),
        (BuiltInRoles.Admin, "管理员", 40, ["*"]),
        (BuiltInRoles.SuperAdmin, "超级管理员", 100, [])
    ];

    public void Seed(IEnumerable<Assembly> permissionAssemblies)
    {
        var defs = PermissionCatalog.Discover(permissionAssemblies.Append(typeof(Permissions).Assembly));
        var existing = db.Queryable<PermissionEntity>().ToList().ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs)
        {
            if (existing.TryGetValue(d.Code, out var e))
            {
                if (e.Name == d.Name && e.Category == d.Category && e.Kind == d.Kind && e.Sort == d.Sort) continue;
                e.Name = d.Name; e.Category = d.Category; e.Kind = d.Kind; e.Sort = d.Sort; e.UpdatedAt = DateTime.Now;
                db.Updateable(e).ExecuteCommand();
            }
            else
            {
                db.Insertable(new PermissionEntity { Code = d.Code, Name = d.Name, Category = d.Category, Kind = d.Kind, Sort = d.Sort }).ExecuteCommand();
            }
        }
        var obsolete = existing.Keys.Except(defs.Select(d => d.Code), StringComparer.OrdinalIgnoreCase).ToList();
        if (obsolete.Count > 0) db.Deleteable<PermissionEntity>().Where(p => obsolete.Contains(p.Code)).ExecuteCommand();

        foreach (var (code, name, level, perms) in Roles)
        {
            if (db.Queryable<RoleEntity>().Any(r => r.Code == code)) continue;
            var id = db.Insertable(new RoleEntity { Code = code, Name = name, Level = level, IsBuiltIn = true }).ExecuteReturnBigIdentity();
            var codes = perms is ["*"]
                ? defs.Where(d => d.Code != Permissions.Flow.Script).Select(d => d.Code)
                : perms;
            var rows = codes.Select(c => new RolePermissionEntity { RoleId = id, PermissionCode = c }).ToList();
            if (rows.Count > 0) db.Insertable(rows).ExecuteCommand();
            logger.LogInformation("已创建内置角色 {Role}", name);
        }

        if (!db.Queryable<UserEntity>().Any(u => u.UserName == "admin"))
        {
            var userId = db.Insertable(new UserEntity
            {
                UserName = "admin",
                DisplayName = "超级管理员",
                PasswordHash = PasswordHasher.Hash(options.DefaultAdminPassword),
                IsBuiltIn = true
            }).ExecuteReturnBigIdentity();
            var roleId = db.Queryable<RoleEntity>().Where(r => r.Code == BuiltInRoles.SuperAdmin).Select(r => r.Id).First();
            db.Insertable(new UserRoleEntity { UserId = userId, RoleId = roleId }).ExecuteCommand();
            logger.LogWarning("已创建默认超级管理员 admin，请尽快修改默认密码");
        }
    }
}
