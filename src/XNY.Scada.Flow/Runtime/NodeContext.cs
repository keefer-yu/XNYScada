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
    /// <summary>存储。</summary>
    private readonly FlowVariableStore _store;

    /// <summary>创建节点上下文。</summary>
    internal NodeContext(FlowInstance instance, NodeDef node, IServiceProvider services, FlowVariableStore store, IFlowRunner runner, ILogger logger)
    {
        Instance = instance;
        Node = node;
        Services = services;
        _store = store;
        Runner = runner;
        Logger = logger;
    }

    /// <summary>实例。</summary>
    public FlowInstance Instance { get; }

    /// <summary>节点。</summary>
    public NodeDef Node { get; }

    /// <summary>服务。</summary>
    public IServiceProvider Services { get; }

    /// <summary>运行器。</summary>
    public IFlowRunner Runner { get; }

    /// <summary>日志。</summary>
    public ILogger Logger { get; }

    /// <summary>点位数量。</summary>
    public ITagService Tags => Services.GetRequiredService<ITagService>();

    /// <summary>获取。</summary>
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>尝试获取。</summary>
    public T? TryGet<T>() => Services.GetService<T>();

    // ----- 属性读取 -----

    /// <summary>属性。</summary>
    public string? Prop(string key) => Node.Get(key);

    /// <summary>要求属性。</summary>
    public string RequireProp(string key) => Node.Get(key) ?? throw new FlowException($"节点 {Node.DisplayName} 缺少属性 {key}");

    /// <summary>属性整数。</summary>
    public int PropInt(string key, int defaultValue)
    {
        var v = Prop(key);
        if (v == null) return defaultValue;
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return Convert.ToInt32(Evaluate(v), CultureInfo.InvariantCulture);
    }

    /// <summary>属性布尔。</summary>
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

    /// <summary>解析赋值。</summary>
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

    /// <summary>获取变量。</summary>
    public object? GetVar(string name) => Instance.Variables.TryGetValue(name, out var v) ? v : null;

    /// <summary>设置变量。</summary>
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

    /// <summary>求值。</summary>
    public object? Evaluate(string expression) => ScadaExpression.Evaluate(expression, Instance.Variables, Resolve);

    /// <summary>求值布尔。</summary>
    public bool EvaluateBool(string expression) => ScadaExpression.EvaluateBool(expression, Instance.Variables, Resolve);

    /// <summary>渲染。</summary>
    public string Render(string template) => ScadaExpression.Render(template, Instance.Variables, Resolve);

    /// <summary>解析。</summary>
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

    /// <summary>读取标记。</summary>
    public async Task<object?> ReadTagAsync(string tag, bool fromDevice, CancellationToken ct)
    {
        if (!fromDevice) return Tags.GetValue(tag)?.Value;
        var r = await Tags.ReadAsync(tag, ct);
        if (!r.IsSuccess) throw new FlowException($"读点位 {tag} 失败: {r.Message}");
        return r.Value;
    }

    /// <summary>写入标记。</summary>
    public async Task WriteTagAsync(string tag, object? value, CancellationToken ct)
    {
        var r = await Tags.WriteAsync(tag, value, ct);
        if (!r.IsSuccess) throw new FlowException($"写点位 {tag}={value} 失败: {r.Message}");
    }
}
