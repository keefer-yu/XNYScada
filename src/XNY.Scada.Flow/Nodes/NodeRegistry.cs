using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace XNY.Scada.Flow.Nodes;

public interface INodeRegistry
{
    IReadOnlyList<NodeDescriptor> Descriptors { get; }

    NodeDescriptor? Find(string type);

    INodeHandler Create(string type, IServiceProvider services);
}

public sealed class NodeRegistry : INodeRegistry
{
    private readonly Dictionary<string, NodeDescriptor> _map = new(StringComparer.OrdinalIgnoreCase);

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

    public IReadOnlyList<NodeDescriptor> Descriptors => _map.Values.OrderBy(d => d.Category).ThenBy(d => d.DisplayName).ToList();

    public NodeDescriptor? Find(string type) => _map.GetValueOrDefault(type);

    public INodeHandler Create(string type, IServiceProvider services)
    {
        var d = Find(type) ?? throw new FlowException($"未知节点类型: {type}");
        return (INodeHandler)ActivatorUtilities.CreateInstance(services, d.HandlerType);
    }
}