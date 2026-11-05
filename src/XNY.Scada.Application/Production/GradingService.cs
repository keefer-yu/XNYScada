using System.Collections.Concurrent;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Expressions;
using XNY.Scada.Domain.Services;

namespace XNY.Scada.Application.Production;

/// <summary>
/// 分档：按优先级依次匹配规则，区间条件全部满足且附加表达式成立即命中。
/// 规则按配方缓存，配方变更时清空缓存。
/// </summary>
public sealed class GradingService : IGradingService
{
    private sealed record CompiledRule(GradingRuleEntity Rule, IReadOnlyList<GradingCondition> Conditions);

    private readonly ISqlSugarClient _db;
    private readonly RecipeService _recipes;
    private readonly ConcurrentDictionary<string, IReadOnlyList<CompiledRule>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public GradingService(ISqlSugarClient db, RecipeService recipes)
    {
        _db = db;
        _recipes = recipes;
        _recipes.RecipesChanged += (_, _) => _cache.Clear();
        _recipes.CurrentRecipeChanged += (_, _) => _cache.Clear();
    }

    public async Task<GradeResult> GradeAsync(IReadOnlyDictionary<string, object?> values, string? recipeCode = null)
    {
        recipeCode ??= _recipes.Current?.Code;
        if (string.IsNullOrEmpty(recipeCode)) return new GradeResult(false, 0, "", true, "NO_RECIPE", "未激活生产配方");
        var rules = await GetRulesAsync(recipeCode);
        if (rules.Count == 0) return new GradeResult(false, 0, "", true, "NO_RULE", $"配方 {recipeCode} 未配置分档规则");

        foreach (var cr in rules)
        {
            if (!Match(cr, values)) continue;
            var r = cr.Rule;
            return new GradeResult(true, r.BinNo, r.BinName, r.IsNg, r.IsNg ? r.NgCode ?? $"BIN{r.BinNo}" : null,
                r.IsNg ? $"NG 档 {r.BinName}" : null);
        }
        return new GradeResult(false, 0, "", true, "OUT_OF_RANGE", "数据不在任何档位范围内");
    }

    public static bool Match(GradingRuleEntity rule, IReadOnlyList<GradingCondition> conditions, IReadOnlyDictionary<string, object?> values) =>
        Match(new CompiledRule(rule, conditions), values);

    private static bool Match(CompiledRule cr, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var c in cr.Conditions)
        {
            var key = values.Keys.FirstOrDefault(k => string.Equals(k, c.Field, StringComparison.OrdinalIgnoreCase));
            var v = key == null ? null : RecipeService.ToDouble(values[key]);
            if (v == null) return false;
            if (c.Min != null && v < c.Min) return false;
            if (c.Max != null && v > c.Max) return false;
        }
        if (!string.IsNullOrWhiteSpace(cr.Rule.Expression))
        {
            return ScadaExpression.EvaluateBool(cr.Rule.Expression, values);
        }
        return true;
    }

    private async Task<IReadOnlyList<CompiledRule>> GetRulesAsync(string recipeCode)
    {
        if (_cache.TryGetValue(recipeCode, out var cached)) return cached;
        var recipe = await _db.Queryable<RecipeEntity>().FirstAsync(r => r.Code == recipeCode);
        if (recipe == null) return [];
        var rules = (await _recipes.GetRulesAsync(recipe.Id))
            .OrderBy(r => r.Priority).ThenBy(r => r.BinNo)
            .Select(r => new CompiledRule(r, Json.Deserialize<List<GradingCondition>>(r.ConditionsJson) ?? []))
            .ToList();
        _cache[recipeCode] = rules;
        return rules;
    }
}
