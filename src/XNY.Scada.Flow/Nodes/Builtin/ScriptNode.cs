using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using XNY.Scada.Device;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Flow.Nodes.Builtin;

/// <summary>脚本全局对象：脚本中可直接使用 Vars / Tags / Log / Ct</summary>
public sealed class ScriptGlobals
{
    /// <summary>变量。</summary>
    public required IDictionary<string, object?> Vars { get; init; }

    /// <summary>点位数量。</summary>
    public required ITagService Tags { get; init; }

    /// <summary>日志。</summary>
    public required Action<string> Log { get; init; }

    /// <summary>取消标记。</summary>
    public required CancellationToken Ct { get; init; }

    /// <summary>获取。</summary>
    public object? Get(string name) => Vars.TryGetValue(name, out var v) ? v : null;

    /// <summary>编号。</summary>
    public double Num(string name) => Convert.ToDouble(Get(name) ?? 0, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>字符串。</summary>
    public string Str(string name) => Convert.ToString(Get(name), System.Globalization.CultureInfo.InvariantCulture) ?? "";
}

/// <summary>编译缓存：相同代码只编译一次</summary>
public sealed class ScriptCompiler
{
    /// <summary>缓存。</summary>
    private readonly ConcurrentDictionary<string, Lazy<ScriptRunner<object>>> _cache = new();

    /// <summary>选项。</summary>
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .AddReferences(typeof(ScriptGlobals).Assembly, typeof(ITagService).Assembly, typeof(Enumerable).Assembly)
        .AddImports("System", "System.Linq", "System.Collections.Generic", "System.Text", "System.Text.RegularExpressions", "System.Threading.Tasks");

    /// <summary>编译。</summary>
    public ScriptRunner<object> Compile(string code) =>
        _cache.GetOrAdd(code, c => new Lazy<ScriptRunner<object>>(() =>
        {
            var script = CSharpScript.Create<object>(c, Options, typeof(ScriptGlobals));
            var errors = script.Compile().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0) throw new FlowException("脚本编译错误: " + string.Join("; ", errors.Select(e => e.ToString())));
            return script.CreateDelegate();
        })).Value;
}

/// <summary>脚本节点。</summary>
/// <param name="compiler">编译器。</param>
[FlowNode("Script", "C# 脚本", "高级", Ports = "next", RequiredPermission = "Flow.Script",
    Description = "可使用 Vars、Tags、Log、Ct；返回字符串作为输出端口名，返回 null 走 next")]
[NodeProperty("Code", "脚本代码", Kind = NodePropertyKind.Code, Required = true, Default = "Vars[\"Result\"] = 1;\nreturn null;")]
[NodeProperty("Ports", "自定义端口", Description = "逗号分隔，供编辑器连线使用")]
public sealed class ScriptNode(ScriptCompiler compiler) : INodeHandler
{
    /// <summary>执行。</summary>
    public async Task<NodeResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var runner = compiler.Compile(ctx.RequireProp("Code"));
        var globals = new ScriptGlobals
        {
            Vars = ctx.Instance.Variables,
            Tags = ctx.Tags,
            Log = m => ctx.Logger.LogInformation("[{Flow}/Script] {Message}", ctx.Instance.FlowCode, m),
            Ct = ct
        };
        var result = await runner(globals, ct);
        return result is string port && port.Length > 0 ? NodeResult.To(port) : NodeResult.Next;
    }
}
