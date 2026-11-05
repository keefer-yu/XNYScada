using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using XNY.Scada.Flow.Nodes;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Flow;

public static class FlowServiceCollectionExtensions
{
    /// <summary>注册流程引擎。pluginAssemblies 中的 INodeHandler 会作为扩展节点加载。</summary>
    public static IServiceCollection AddScadaFlow(this IServiceCollection services, IEnumerable<Assembly>? pluginAssemblies = null)
    {
        var assemblies = (pluginAssemblies ?? []).ToList();
        services.AddSingleton<INodeRegistry>(_ => new NodeRegistry(assemblies));
        services.AddSingleton<FlowVariableStore>();
        services.AddSingleton<FlowDebugger>();
        services.AddSingleton<FlowExecutor>();
        services.AddSingleton<FlowEngine>();
        services.AddSingleton<IFlowEngine>(sp => sp.GetRequiredService<FlowEngine>());
        services.AddSingleton<Nodes.Builtin.ScriptCompiler>();
        return services;
    }
}
