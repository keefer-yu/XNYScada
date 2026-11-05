using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace XNY.Scada.Flow.Nodes;

/// <summary>按节点类型查找描述并创建处理器。类型来自 <see cref="FlowNodeAttribute"/>。</summary>
public interface INodeRegistry
{
    /// <summary>描述。</summary>
    IReadOnlyList<NodeDescriptor> Descriptors { get; }

    /// <summary>查找。</summary>
    NodeDescriptor? Find(string type);

    /// <summary>创建。</summary>
    INodeHandler Create(string type, IServiceProvider services);
}

/// <summary>
/// 扫描程序集中带 <see cref="FlowNodeAttribute"/> 的 <see cref="INodeHandler"/>，
/// 合并属性、端口和所需权限，供设计器与执行器共用。
/// </summary>
public sealed class NodeRegistry : INodeRegistry
{
    /// <summary>映射。</summary>
    private readonly Dictionary<string, NodeDescriptor> _map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>创建节点注册。</summary>
    public NodeRegistry(IEnumerable<Assembly> assemblies)
    {
        foreach (var asm in assemblies.Append(typeof(NodeRegistry).Assembly).Distinct())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var t in types)
            {
                if (t.IsAbstract || !typeof(INodeHandler).IsAssignableFrom(t)) continue;
                var a = t.GetCustomAttribute<FlowNodeAttribute>();
                if (a == null) continue;
                var props = t.GetCustomAttributes<NodePropertyAttribute>(true)
                    .GroupBy(p => p.Key).Select(g => g.First())
                    .OrderBy(p => p.Order)
                    .Select(p => new NodePropertyDescriptor(p.Key, p.DisplayName, p.Kind, p.Required, p.Default,
                        p.Choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), p.Description, p.Order))
                    .ToList();
                var ports = a.Ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _map[a.Type] = new NodeDescriptor(a.Type, a.DisplayName, a.Category, a.Description, ports, props, a.RequiredPermission, t);
            }
        }
    }

    /// <summary>描述。</summary>
    public IReadOnlyList<NodeDescriptor> Descriptors => _map.Values.OrderBy(d => d.Category).ThenBy(d => d.DisplayName).ToList();

    /// <summary>查找。</summary>
    public NodeDescriptor? Find(string type) => _map.GetValueOrDefault(type);

    /// <summary>创建。</summary>
    public INodeHandler Create(string type, IServiceProvider services)
    {
        var d = Find(type) ?? throw new FlowException($"未知节点类型: {type}");
        return (INodeHandler)ActivatorUtilities.CreateInstance(services, d.HandlerType);
    }
}