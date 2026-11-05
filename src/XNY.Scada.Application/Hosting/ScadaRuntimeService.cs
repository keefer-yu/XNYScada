using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XNY.Scada.Application.Production;
using XNY.Scada.Application.Settings;
using XNY.Scada.Application.Templates;
using XNY.Scada.Data;
using XNY.Scada.Device;
using XNY.Scada.Device.Runtime;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Mes;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Hosting;

/// <summary>按顺序启动 SCADA 运行时：数据库 → 权限种子 → 参数/配方 → 模板 → 通讯 → 报警 → MES → 流程引擎</summary>
public sealed class ScadaRuntimeService(
    DatabaseInitializer database,
    SecuritySeeder securitySeeder,
    SystemParamService parameters,
    RecipeService recipes,
    MachineTemplateService templates,
    IDeviceManager devices,
    AlarmService alarms,
    IMesClient mes,
    IFlowEngine flowEngine,
    IdleLogoutMonitor idleMonitor,
    ScadaHostOptions options,
    ILogger<ScadaRuntimeService> logger) : IHostedService
{
    public static event EventHandler<string>? StartupProgress;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Report("初始化数据库");
        database.Initialize();
        Report("同步权限");
        securitySeeder.Seed(options.PermissionAssemblies);
        await parameters.LoadAsync();
        idleMonitor.TimeoutMinutes = parameters.GetInt("AutoLogoutMinutes", idleMonitor.TimeoutMinutes);

        if (options.AutoImportTemplate)
        {
            Report("检查机型模板");
            await templates.EnsureInitializedAsync();
        }
        await recipes.LoadActiveAsync();

        Report("启动设备通讯");
        await devices.StartAsync(cancellationToken);
        await alarms.LoadAsync();
        await mes.ReloadAsync();

        if (options.StartFlowEngine)
        {
            Report("启动流程引擎");
            await flowEngine.StartAsync(cancellationToken);
        }

        try
        {
            database.ApplyRetention();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "数据清理失败");
        }
        Report("就绪");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await flowEngine.StopAsync();
        await devices.StopAsync();
    }

    private void Report(string message)
    {
        logger.LogInformation("启动: {Step}", message);
        StartupProgress?.Invoke(this, message);
    }
}
