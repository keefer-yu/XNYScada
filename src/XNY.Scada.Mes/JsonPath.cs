using System.Text.Json;
using System.Text.RegularExpressions;
using XNY.Scada.Domain;

namespace XNY.Scada.Mes;

/// <summary>简易 JSON 路径：data.items[0].code</summary>
public static partial class JsonPath
{
    [GeneratedRegex(@"^(?<name>[^\[\]]*)(\[(?<idx>\d+)\])*$")]
    private static partial Regex SegmentRegex();

    public static object? Select(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = SegmentRegex().Match(segment);
            if (!m.Success) return null;
            var name = m.Groups["name"].Value;
            if (current.ValueKind == JsonValueKind.String && TryParseEmbedded(current.GetString(), out var embedded)) current = embedded;
            if (name.Length > 0)
            {
                if (current.ValueKind != JsonValueKind.Object || !TryGetPropertyIgnoreCase(current, name, out current)) return null;
            }
            foreach (Capture c in m.Groups["idx"].Captures)
            {
                var i = int.Parse(c.Value);
                if (current.ValueKind != JsonValueKind.Array || i >= current.GetArrayLength()) return null;
                current = current[i];
            }
        }
        return Json.ToClr(current);
    }

    /// <summary>WebService 常把 JSON 作为字符串返回（如 &lt;Result&gt;{"code":0}&lt;/Result&gt;），路径可继续深入</summary>
    private static bool TryParseEmbedded(string? text, out JsonElement element)
    {
        element = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text[0] is not ('{' or '[')) return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            element = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }
}
