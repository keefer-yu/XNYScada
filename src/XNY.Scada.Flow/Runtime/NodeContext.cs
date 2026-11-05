using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XNY.Scada.Device;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Expressions;
using XNY.Scada.Domain.Services;
using XNY.Scada.Flow.Model;

namespace XNY.Scada.Flow.Runtime;

/// <summary>节点执行上下文：访问变量、表达式求值、服务</summary>
public sealed class NodeContext
{
    private readonly FlowVariableStore _store;

    internal NodeContext(FlowInstance instance, NodeDef node, IServiceProvider services, FlowVariableStore store, IFlowRunner runner, ILogger logger)
    {
        Instance = instance;
        Node = node;
        Services = services;
        _store = store;
        Runner = runner;
        Logger = logger;
    }

    public FlowInstance Instance { get; }

    public NodeDef Node { get; }

    public IServiceProvider Services { get; }

    public IFlowRunner Runner { get; }

    public ILogger Logger { get; }

    public ITagService Tags => Services.GetRequiredService<ITagService>();

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public T? TryGet<T>() => Services.GetService<T>();

    // ----- 属性读取 -----

    public string? Prop(string key) => Node.Get(key);

    public string RequireProp(string key) => Node.Get(key) ?? throw new FlowException($"节点 {Node.DisplayName} 缺少属性 {key}");

    public int PropInt(string key, int defaultValue)
    {
        var v = Prop(key);
        if (v == null) return defaultValue;
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return Convert.ToInt32(Evaluate(v), CultureInfo.InvariantCulture);
    }

    public bool PropBool(string key, bool defaultValue)
    {
        var v = Prop(key);
        return v == null ? defaultValue : bool.TryParse(v, out var b) ? b : EvaluateBool(v);
    }

    /// <summary>属性作为表达式求值</summary>
    public object? EvalProp(string key) => Prop(key) is { } e ? Evaluate(e) : null;

    /// <summary>属性作为模板渲染</summary>
    public string RenderProp(string key) => Prop(key) is { } t ? Render(t) : "";

    /// <summary>解析多行赋值属性（名称 = 表达式），返回求值结果</summary>
    public Dictionary<string, object?> EvalAssignments(string key)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, expr) in ParseAssignments(Prop(key)))
        {
            result[name] = Evaluate(expr);
        }
        return result;
    }

    public static IEnumerable<(string Name, string Expression)> ParseAssignments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var raw in text.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) throw new FlowException($"赋值格式错误（应为 名称 = 表达式）：{line}");
            yield return (line[..idx].Trim(), line[(idx + 1)..].Trim());
        }
    }

    // ----- 变量 -----

    public object? GetVar(string name) => Instance.Variables.TryGetValue(name, out var v) ? v : null;

    public void SetVar(string name, object? value, VariableScope scope = VariableScope.Instance)
    {
        switch (scope)
        {
            case VariableScope.Global:
                _store.SetGlobal(name, value);
                break;
            case VariableScope.Station:
                _store.SetStation(Instance.Station, name, value);
                break;
            default:
                Instance.Variables[name] = value;
                break;
        }
    }

    // ----- 表达式 -----

    public object? Evaluate(string expression) => ScadaExpression.Evaluate(expression, Instance.Variables, Resolve);

    public bool EvaluateBool(string expression) => ScadaExpression.EvaluateBool(expression, Instance.Variables, Resolve);

    public string Render(string template) => ScadaExpression.Render(template, Instance.Variables, Resolve);

    public object? Resolve(string kind, string name)
    {
        switch (kind.ToLowerInvariant())
        {
            case "tag":
                var tv = Tags.GetValue(name) ?? throw new FlowException($"点位不存在: {name}");
                return tv.Value;
            case "var":
                return GetVar(name);
            case "global":
                return _store.Global.TryGetValue(name, out var g) ? g : null;
            case "station":
                return _store.Station(Instance.Station).TryGetValue(name, out var s) ? s : null;
            case "recipe":
                return TryGet<IRecipeProvider>()?.GetParam(name);
            case "param":
                return TryGet<IFlowEnvironment>()?.GetParam(name);
            case "sys":
                return name.ToLowerInvariant() switch
                {
                    "now" => DateTime.Now,
                    "station" => Instance.Station,
                    "instanceid" => Instance.Id,
                    "flowcode" => Instance.FlowCode,
                    _ => null
                };
            default:
                throw new FlowException($"未知占位符类型: {kind}");
        }
    }

    // ----- 点位读写辅助 -----

    public async Task<object?> ReadTagAsync(string tag, bool fromDevice, CancellationToken ct)
    {
        if (!fromDevice) return Tags.GetValue(tag)?.Value;
        var r = await Tags.ReadAsync(tag, ct);
        if (!r.IsSuccess) throw new FlowException($"读点位 {tag} 失败: {r.Message}");
        return r.Value;
    }

    public async Task WriteTagAsync(string tag, object? value, CancellationToken ct)
    {
        var r = await Tags.WriteAsync(tag, value, ct);
        if (!r.IsSuccess) throw new FlowException($"写点位 {tag}={value} 失败: {r.Message}");
    }
}
