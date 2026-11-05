using System.Globalization;
using System.Text.RegularExpressions;
using NCalc;

namespace XNY.Scada.Domain.Expressions;

/// <summary>
/// 解析 {Kind:Name} 占位符的回调，例如 {Tag:Dev1.Bin}、{Recipe:MaxIR}、{Var:BinNo}、{Global:Count}
/// </summary>
public delegate object? PlaceholderResolver(string kind, string name);

/// <summary>
/// 统一表达式求值（基于 NCalc）。支持：
/// 1. 变量直接引用：Voltage &gt; 3.2，或带特殊字符时用 [Dev1.Value]
/// 2. 占位符：{Tag:Dev1.Value} &gt; 0
/// 3. 扩展函数：len, contains, startsWith, endsWith, substr, regex, concat, isEmpty, toNumber, toString, now, format
/// </summary>
public static partial class ScadaExpression
{
    [GeneratedRegex(@"\{(?<kind>[A-Za-z]+):(?<name>[^{}]+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\$\{(?<name>[^{}]+)\}")]
    private static partial Regex TemplateRegex();

    public static object? Evaluate(string expression, IReadOnlyDictionary<string, object?>? variables = null, PlaceholderResolver? resolver = null)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;

        var placeholders = new Dictionary<string, object?>();
        var text = PlaceholderRegex().Replace(expression, m =>
        {
            var key = $"__p{placeholders.Count}";
            placeholders[key] = Normalize(resolver?.Invoke(m.Groups["kind"].Value, m.Groups["name"].Value.Trim()));
            return $"[{key}]";
        });

        var e = new Expression(text, CultureInfo.InvariantCulture)
        {
            Options = ExpressionOptions.IgnoreCaseAtBuiltInFunctions | ExpressionOptions.AllowNullParameter
        };
        if (variables != null)
        {
            foreach (var kv in variables) e.Parameters[kv.Key] = Normalize(kv.Value);
        }
        foreach (var kv in placeholders) e.Parameters[kv.Key] = kv.Value;
        e.EvaluateFunction += EvaluateFunction;
        return e.Evaluate();
    }

    public static bool EvaluateBool(string expression, IReadOnlyDictionary<string, object?>? variables = null, PlaceholderResolver? resolver = null)
    {
        var r = Evaluate(expression, variables, resolver);
        return r switch
        {
            null => false,
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
            IConvertible c => Convert.ToDouble(c, CultureInfo.InvariantCulture) != 0,
            _ => false
        };
    }

    /// <summary>文本模板：替换 ${name} 与 {Kind:Name}，不做表达式计算</summary>
    public static string Render(string template, IReadOnlyDictionary<string, object?>? variables = null, PlaceholderResolver? resolver = null, Func<object?, string>? formatter = null)
    {
        formatter ??= ToInvariantString;
        var text = TemplateRegex().Replace(template, m =>
        {
            var name = m.Groups["name"].Value.Trim();
            if (name.Contains(':'))
            {
                var parts = name.Split(':', 2);
                return formatter(resolver?.Invoke(parts[0], parts[1]));
            }
            return formatter(variables != null && variables.TryGetValue(name, out var v) ? v : null);
        });
        return PlaceholderRegex().Replace(text, m => formatter(resolver?.Invoke(m.Groups["kind"].Value, m.Groups["name"].Value.Trim())));
    }

    public static string ToInvariantString(object? value) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>NCalc 对 float/short 等类型支持不一致，统一为 double/long/string/bool</summary>
    private static object? Normalize(object? v) => v switch
    {
        null => null,
        float f => (double)f,
        short or ushort or int or uint or byte or sbyte => Convert.ToInt64(v, CultureInfo.InvariantCulture),
        decimal m => (double)m,
        _ => v
    };

    private static void EvaluateFunction(string name, NCalc.Handlers.FunctionEventArgs args)
    {
        object?[] P() => Enumerable.Range(0, args.Parameters.Count).Select(i => args.Parameters.Evaluate(i)).ToArray();
        string S(object? o) => ToInvariantString(o);

        switch (name.ToLowerInvariant())
        {
            case "len":
                args.Result = (long)S(P()[0]).Length;
                break;
            case "contains":
            {
                var p = P();
                args.Result = S(p[0]).Contains(S(p[1]), StringComparison.Ordinal);
                break;
            }
            case "startswith":
            {
                var p = P();
                args.Result = S(p[0]).StartsWith(S(p[1]), StringComparison.Ordinal);
                break;
            }
            case "endswith":
            {
                var p = P();
                args.Result = S(p[0]).EndsWith(S(p[1]), StringComparison.Ordinal);
                break;
            }
            case "substr":
            {
                var p = P();
                var s = S(p[0]);
                var start = Math.Clamp(Convert.ToInt32(p[1], CultureInfo.InvariantCulture), 0, s.Length);
                var len = p.Length > 2 ? Math.Clamp(Convert.ToInt32(p[2], CultureInfo.InvariantCulture), 0, s.Length - start) : s.Length - start;
                args.Result = s.Substring(start, len);
                break;
            }
            case "regex":
            {
                var p = P();
                args.Result = Regex.IsMatch(S(p[0]), S(p[1]));
                break;
            }
            case "concat":
                args.Result = string.Concat(P().Select(S));
                break;
            case "isempty":
                args.Result = string.IsNullOrWhiteSpace(S(P()[0]));
                break;
            case "tonumber":
                args.Result = double.TryParse(S(P()[0]), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d;
                break;
            case "tostring":
                args.Result = S(P()[0]);
                break;
            case "now":
                args.Result = DateTime.Now;
                break;
            case "format":
            {
                var p = P();
                args.Result = p[0] is IFormattable f ? f.ToString(S(p[1]), CultureInfo.InvariantCulture) : S(p[0]);
                break;
            }
        }
    }
}
