using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using XNY.Scada.Application.Devices;
using XNY.Scada.Application.Flows;
using XNY.Scada.Application.Mes;
using XNY.Scada.Application.Production;
using XNY.Scada.Application.Settings;
using XNY.Scada.Application.Templates;
using XNY.Scada.Data;
using XNY.Scada.Device.Hsl;
using XNY.Scada.Device.Runtime;
using XNY.Scada.Domain.Services;
using XNY.Scada.Flow;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Mes;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Hosting;

public sealed class ScadaHostOptions
{
    /// <summary>扫描 [Permission] 的程序集（界面层 ViewModel 所在程序集等）</summary>
    public List<Assembly> PermissionAssemblies { get; } = [];

    public bool AutoImportTemplate { get; set; } = true;

    public bool StartFlowEngine { get; set; } = true;

    public string PluginDirectory { get; set; } = "plugins";
}

public static class ScadaHost
{
    /// <summary>注册 SCADA 全部核心服务（与界面无关）</summary>
    public static IServiceCollection AddScadaCore(this IServiceCollection services, IConfiguration configuration, ScadaHostOptions? options = null)
    {
        options ??= new ScadaHostOptions();
        services.AddSingleton(options);
        HslLicense.Activate(configuration["Hsl:AuthorizationCode"]);

        services.AddScadaData(configuration);
        services.AddScadaSecurity(configuration);
        services.AddDeviceRuntime(options.PluginDirectory);
        services.AddScadaFlow(PluginLoader.Load(options.PluginDirectory));

        // 运行时编排先于 MES 后台任务注册，保证数据库已初始化
        services.AddSingleton<ScadaRuntimeService>();
        services.AddHostedService(sp => sp.GetRequiredService<ScadaRuntimeService>());
        services.AddScadaMes(configuration);

        services.AddSingleton(sp => new Lazy<IFlowEngine>(sp.GetRequiredService<IFlowEngine>));
        services.AddSingleton<FlowDefinitionService>();
        services.AddSingleton<IFlowDefinitionStore>(sp => sp.GetRequiredService<FlowDefinitionService>());
        services.AddSingleton<IFlowInstanceLogSink, FlowInstanceLogSink>();
        services.AddSingleton<FlowLogQueryService>();

        services.AddSingleton<SystemParamService>();
        services.AddSingleton<IFlowEnvironment>(sp => sp.GetRequiredService<SystemParamService>());

        services.AddSingleton<RecipeService>();
        services.AddSingleton(sp => new Lazy<RecipeService>(sp.GetRequiredService<RecipeService>));
        services.AddSingleton<IRecipeProvider>(sp => sp.GetRequiredService<RecipeService>());
        services.AddSingleton<IGradingService, GradingService>();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<IProductionService, ProductionService>();
        services.AddSingleton<ProductionQueryService>();

        services.AddSingleton<DeviceConfigService>();
        services.AddSingleton<AlarmQueryService>();
        services.AddSingleton<MesConfigService>();
        services.AddSingleton<MachineTemplateService>();

        services.AddSingleton<IUserInteraction, NullUserInteraction>();
        return services;
    }

    /// <summary>创建通用主机：appsettings.json + Serilog（控制台 + 按天滚动文件）</summary>
    public static IHostBuilder CreateBuilder(string[] args, Action<IServiceCollection, IConfiguration>? configureUi = null, ScadaHostOptions? options = null)
    {
        return Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog((ctx, cfg) =>
            {
                cfg.ReadFrom.Configuration(ctx.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "scada-.log"),
                        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
            })
            .ConfigureServices((ctx, services) =>
            {
                services.AddScadaCore(ctx.Configuration, options);
                configureUi?.Invoke(services, ctx.Configuration);
            });
    }
}
