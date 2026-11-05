using System.Reflection;
using System.Runtime.Loader;

namespace XNY.Scada.Device.Runtime;

/// <summary>从插件目录加载程序集，供驱动注册表和流程节点注册表扫描</summary>
public static class PluginLoader
{
    /// <summary>同步。</summary>
    private static readonly object Sync = new();
    /// <summary>已加载。</summary>
    private static List<Assembly>? _loaded;

    /// <summary>加载。</summary>
    public static IReadOnlyList<Assembly> Load(string directory)
    {
        lock (Sync)
        {
            if (_loaded != null) return _loaded;
            _loaded = [];
            var dir = Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory);
            if (!Directory.Exists(dir)) return _loaded;
            foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    var name = AssemblyName.GetAssemblyName(file);
                    var existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);
                    _loaded.Add(existing ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(file));
                }
                catch (BadImageFormatException)
                {
                    // 非托管 DLL，忽略
                }
            }
            return _loaded;
        }
    }

    /// <summary>安全获取类型。</summary>
    public static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }
}
