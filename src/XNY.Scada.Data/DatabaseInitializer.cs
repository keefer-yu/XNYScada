using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Data;

public sealed class DatabaseInitializer(ISqlSugarClient db, DatabaseOptions options, ILogger<DatabaseInitializer> logger)
{
    public static readonly Type[] EntityTypes =
    [
        typeof(DeviceEntity), typeof(TagGroupEntity), typeof(TagEntity),
        typeof(AlarmDefinitionEntity), typeof(AlarmRecordEntity),
        typeof(UserEntity), typeof(RoleEntity), typeof(PermissionEntity), typeof(UserRoleEntity),
        typeof(RolePermissionEntity), typeof(RoleDataScopeEntity), typeof(AuditLogEntity), typeof(SystemParamEntity),
        typeof(FlowDefinitionEntity), typeof(FlowVersionEntity), typeof(FlowInstanceLogEntity),
        typeof(RecipeEntity), typeof(GradingRuleEntity), typeof(TrayEntity), typeof(TrayCellEntity),
        typeof(MesInterfaceEntity), typeof(MesCallLogEntity), typeof(MesOutboxEntity)
    ];

    public static readonly Type[] SplitEntityTypes = [typeof(CellRecordEntity), typeof(NgRecordEntity)];

    public static readonly Dictionary<string, (string Value, string Group, string Description)> DefaultParams = new()
    {
        ["MachineType"] = ("Grading", "Machine", "机型：TrayAssembly/TrayDisassembly/Grading/NgSorting"),
        ["MachineName"] = ("分档机-01", "Machine", "设备名称"),
        ["Station"] = ("ST01", "Machine", "工位号"),
        ["IdealCycleSeconds"] = ("2", "Production", "理想节拍（秒），用于 OEE 性能计算"),
        ["LayoutJson"] = ("{}", "Machine", "主界面布局配置"),
        ["AutoLogoutMinutes"] = ("15", "Security", "无操作自动登出（分钟），0 不登出")
    };

    public void Initialize()
    {
        logger.LogInformation("初始化数据库结构 ({DbType})", options.DbType);
        if (db.CurrentConnectionConfig.DbType != DbType.Sqlite)
        {
            db.DbMaintenance.CreateDatabase();
        }
        db.CodeFirst.InitTables(EntityTypes);
        foreach (var t in SplitEntityTypes)
        {
            db.CodeFirst.SplitTables().InitTables(t);
        }

        var existing = db.Queryable<SystemParamEntity>().Select(p => p.Key).ToList().ToHashSet();
        var missing = DefaultParams.Where(kv => !existing.Contains(kv.Key))
            .Select(kv => new SystemParamEntity { Key = kv.Key, Value = kv.Value.Value, Group = kv.Value.Group, Description = kv.Value.Description })
            .ToList();
        if (missing.Count > 0) db.Insertable(missing).ExecuteCommand();
    }

    /// <summary>删除超出保留期的分表</summary>
    public int ApplyRetention()
    {
        if (options.RetentionMonths <= 0) return 0;
        var cutoff = DateTime.Now.AddMonths(-options.RetentionMonths);
        var dropped = 0;
        foreach (var t in SplitEntityTypes)
        {
            var tables = db.SplitHelper(t).GetTables();
            foreach (var table in tables.Where(x => x.Date < cutoff))
            {
                db.DbMaintenance.DropTable(table.TableName);
                logger.LogInformation("已清理过期数据表 {Table}", table.TableName);
                dropped++;
            }
        }
        return dropped;
    }
}
