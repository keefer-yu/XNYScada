using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Nodes;

namespace XNY.Scada.Flow.Runtime;

/// <summary>校验问题。</summary>
/// <param name="IsError">是否错误。</param>
/// <param name="NodeId">节点标识。</param>
/// <param name="Message">消息。</param>
public sealed record ValidationIssue(bool IsError, string? NodeId, string Message)
{
    /// <summary>转换为字符串。</summary>
    public override string ToString() => $"{(IsError ? "错误" : "警告")}{(NodeId == null ? "" : $" [{NodeId}]")}: {Message}";
}

/// <summary>流校验器。</summary>
public static class FlowValidator
{
    /// <summary>校验。</summary>
    public static IReadOnlyList<ValidationIssue> Validate(FlowDocument doc, INodeRegistry registry)
    {
        var issues = new List<ValidationIssue>();
        void Err(string? node, string msg) => issues.Add(new ValidationIssue(true, node, msg));
        void Warn(string? node, string msg) => issues.Add(new ValidationIssue(false, node, msg));

        if (string.IsNullOrWhiteSpace(doc.Code)) Err(null, "流程编码不能为空");
        if (doc.Nodes.Count == 0) Err(null, "流程没有任何节点");

        var duplicates = doc.Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var d in duplicates) Err(d, "节点 ID 重复");

        if (doc.Nodes.Count > 0 && doc.FindNode(doc.StartNodeId) == null) Err(null, $"起始节点 {doc.StartNodeId} 不存在");

        foreach (var n in doc.Nodes)
        {
            var d = registry.Find(n.Type);
            if (d == null)
            {
                Err(n.Id, $"未知节点类型 {n.Type}");
                continue;
            }
            foreach (var p in d.Properties.Where(p => p.Required && string.IsNullOrWhiteSpace(n.Get(p.Key))))
            {
                Err(n.Id, $"{n.DisplayName} 缺少必填属性「{p.DisplayName}」");
            }
            foreach (var p in d.Properties.Where(p => p.Kind == NodePropertyKind.Assignments))
            {
                try { _ = NodeContext.ParseAssignments(n.Get(p.Key)).ToList(); }
                catch (FlowException ex) { Err(n.Id, ex.Message); }
            }
        }

        var ids = doc.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var e in doc.Edges)
        {
            if (!ids.Contains(e.From)) Err(e.From, $"连线起点 {e.From} 不存在");
            if (!ids.Contains(e.To)) Err(e.To, $"连线终点 {e.To} 不存在");
        }
        foreach (var g in doc.Edges.GroupBy(e => (e.From, e.Port.ToLowerInvariant())).Where(g => g.Count() > 1))
        {
            Err(g.Key.From, $"端口 {g.Key.Item2} 存在多条连线");
        }

        // 可达性检查
        if (doc.FindNode(doc.StartNodeId) != null)
        {
            var reached = new HashSet<string>();
            var stack = new Stack<string>([doc.StartNodeId]);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!reached.Add(id)) continue;
                foreach (var e in doc.Edges.Where(e => e.From == id)) stack.Push(e.To);
            }
            foreach (var n in doc.Nodes.Where(n => !reached.Contains(n.Id))) Warn(n.Id, $"{n.DisplayName} 不可达");
        }

        foreach (var t in doc.Triggers)
        {
            if (t.Type is TriggerType.TagRisingEdge or TriggerType.TagFallingEdge or TriggerType.TagChanged && string.IsNullOrWhiteSpace(t.Tag))
                Err(null, $"触发器 {t.Type} 未指定点位");
            if (t.Type == TriggerType.Timer && t.IntervalMs <= 0) Err(null, "定时触发器周期必须大于 0");
            if (t.Type is TriggerType.FlowCompleted or TriggerType.MesCallback && string.IsNullOrWhiteSpace(t.Source))
                Err(null, $"触发器 {t.Type} 未指定来源");
        }
        return issues;
    }
}
