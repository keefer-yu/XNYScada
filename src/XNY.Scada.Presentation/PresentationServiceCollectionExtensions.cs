using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XNY.Scada.Application.Hosting;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Presentation.ViewModels;

namespace XNY.Scada.Presentation;

/// <summary>表示层服务集合的扩展方法。</summary>
public static class PresentationServiceCollectionExtensions
{
    /// <summary>
    /// 注册共享 ViewModel。界面层需另行注册 <see cref="IDialogService"/> 与 <see cref="IUiDispatcher"/>。
    /// 页面 ViewModel 为单例：切换页面时保留筛选条件与编辑状态。
    /// </summary>
    public static IServiceCollection AddScadaPresentation(this IServiceCollection services)
    {
        services.AddSingleton<ViewModelContext>();
        services.AddSingleton<UiUserInteraction>();
        services.Replace(ServiceDescriptor.Singleton<IUserInteraction>(sp => sp.GetRequiredService<UiUserInteraction>()));

        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<ShellViewModel>());

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<AlarmViewModel>();
        services.AddSingleton<ProductionViewModel>();
        services.AddSingleton<RecipeViewModel>();
        services.AddSingleton<FlowMonitorViewModel>();
        services.AddSingleton<FlowDesignerViewModel>();
        services.AddSingleton<DeviceConfigViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MesViewModel>();
        services.AddSingleton<UserAdminViewModel>();
        services.AddSingleton<AuditViewModel>();
        services.AddSingleton<SystemViewModel>();
        return services;
    }

    /// <summary>界面程序使用的默认主机选项</summary>
    public static ScadaHostOptions CreateHostOptions()
    {
        var options = new ScadaHostOptions();
        options.PermissionAssemblies.Add(typeof(PresentationServiceCollectionExtensions).Assembly);
        return options;
    }
}
