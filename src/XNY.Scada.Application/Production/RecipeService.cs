using System.Globalization;
using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Production;

/// <summary>分档条件。</summary>
public sealed class GradingCondition
{
    /// <summary>字段。</summary>
    public string Field { get; set; } = "";
    /// <summary>最小。</summary>
    public double? Min { get; set; }
    /// <summary>最大。</summary>
    public double? Max { get; set; }
}

/// <summary>配方管理：编辑→提交审批→审批→激活；修改已审批配方会生成新版本并回到草稿</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="permissions">权限。</param>
/// <param name="currentUser">当前用户。</param>
/// <param name="audit">审计。</param>
public sealed class RecipeService(ISqlSugarClient db, IPermissionService permissions, ICurrentUser currentUser, IAuditService audit) : IRecipeProvider
{
    /// <summary>当前。</summary>
    private RecipeSnapshot? _current;
    /// <summary>料盒容量。</summary>
    private Dictionary<int, int> _boxCapacity = new();

    /// <summary>当前配方发生变化时触发。</summary>
    public event EventHandler? CurrentRecipeChanged;

    /// <summary>配方数量发生变化时触发。</summary>
    public event EventHandler? RecipesChanged;

    /// <summary>当前值。</summary>
    public RecipeSnapshot? Current => _current;

    /// <summary>获取参数。</summary>
    public object? GetParam(string name) => _current?.Params.GetValueOrDefault(name);

    /// <summary>获取料盒容量。</summary>
    public int GetBoxCapacity(int binNo) => _boxCapacity.GetValueOrDefault(binNo);

    /// <summary>加载活动。</summary>
    public async Task LoadActiveAsync()
    {
        var r = await db.Queryable<RecipeEntity>().FirstAsync(x => x.IsActive);
        _current = r == null ? null : ToSnapshot(r);
        _boxCapacity = r == null
            ? new()
            : (await db.Queryable<GradingRuleEntity>().Where(g => g.RecipeId == r.Id).ToListAsync())
                .GroupBy(g => g.BinNo).ToDictionary(g => g.Key, g => g.Max(x => x.BoxCapacity));
        CurrentRecipeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>转换为快照。</summary>
    private static RecipeSnapshot ToSnapshot(RecipeEntity r) =>
        new(r.Id, r.Code, r.Name, r.ProductModel, r.Version, Json.ToDictionary(r.ParamsJson));

    /// <summary>列出。</summary>
    public Task<List<RecipeEntity>> ListAsync() => db.Queryable<RecipeEntity>().OrderBy(r => r.Code).ToListAsync();

    /// <summary>获取规则。</summary>
    public Task<List<GradingRuleEntity>> GetRulesAsync(long recipeId) =>
        db.Queryable<GradingRuleEntity>().Where(g => g.RecipeId == recipeId).OrderBy(g => g.Priority).ToListAsync();

    /// <summary>保存。</summary>
    public async Task<RecipeEntity> SaveAsync(RecipeEntity recipe, IReadOnlyList<GradingRuleEntity> rules, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Recipe.Edit);
        if (string.IsNullOrWhiteSpace(recipe.Code)) throw new InvalidOperationException("配方编码不能为空");
        _ = Json.ToDictionary(recipe.ParamsJson);
        foreach (var rule in rules) ValidateRule(rule);
        if (await db.Queryable<RecipeEntity>().AnyAsync(r => r.Code == recipe.Code && r.Id != recipe.Id))
            throw new InvalidOperationException($"配方编码 {recipe.Code} 已存在");

        string? oldJson = null;
        if (recipe.Id == 0)
        {
            recipe.Status = RecipeStatus.Draft;
            recipe.Version = 1;
            recipe.IsActive = false;
            recipe.Id = await db.Insertable(recipe).ExecuteReturnBigIdentityAsync();
        }
        else
        {
            var old = await db.Queryable<RecipeEntity>().InSingleAsync(recipe.Id);
            oldJson = Json.Serialize(new { old.ParamsJson, Rules = await GetRulesAsync(old.Id) });
            if (old.Status == RecipeStatus.Approved) recipe.Version = old.Version + 1;
            recipe.Status = RecipeStatus.Draft;
            recipe.ApprovedBy = null;
            recipe.ApprovedAt = null;
            recipe.IsActive = old.IsActive;
            recipe.UpdatedAt = DateTime.Now;
            await db.Updateable(recipe).IgnoreColumns(r => r.CreatedAt).ExecuteCommandAsync();
        }

        await db.Deleteable<GradingRuleEntity>().Where(g => g.RecipeId == recipe.Id).ExecuteCommandAsync();
        foreach (var rule in rules)
        {
            rule.Id = 0;
            rule.RecipeId = recipe.Id;
        }
        if (rules.Count > 0) await db.Insertable(rules.ToList()).ExecuteCommandAsync();

        await audit.LogAsync(AuditActions.RecipeChange, $"recipe:{recipe.Code}:v{recipe.Version}", oldJson,
            Json.Serialize(new { recipe.ParamsJson, Rules = rules }));
        RecipesChanged?.Invoke(this, EventArgs.Empty);
        return recipe;
    }

    /// <summary>校验规则。</summary>
    public static void ValidateRule(GradingRuleEntity rule)
    {
        var conditions = Json.Deserialize<List<GradingCondition>>(rule.ConditionsJson) ?? [];
        foreach (var c in conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Field)) throw new InvalidOperationException($"档位 {rule.BinNo} 条件字段为空");
            if (c.Min > c.Max) throw new InvalidOperationException($"档位 {rule.BinNo} 字段 {c.Field} 下限大于上限");
        }
    }

    /// <summary>提交。</summary>
    public async Task SubmitAsync(long recipeId)
    {
        permissions.Demand(Permissions.Recipe.Edit);
        await db.Updateable<RecipeEntity>().SetColumns(r => r.Status == RecipeStatus.PendingApproval).Where(r => r.Id == recipeId).ExecuteCommandAsync();
        RecipesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>审批（signer 为电子签名人，不能与最后修改人为同一人时由界面强制）</summary>
    public async Task ApproveAsync(long recipeId, string signer)
    {
        permissions.Demand(Permissions.Recipe.Approve);
        var r = await db.Queryable<RecipeEntity>().InSingleAsync(recipeId);
        if (r.Status != RecipeStatus.PendingApproval) throw new InvalidOperationException("只有待审批的配方可以审批");
        r.Status = RecipeStatus.Approved;
        r.ApprovedBy = signer;
        r.ApprovedAt = DateTime.Now;
        await db.Updateable(r).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.RecipeApprove, $"recipe:{r.Code}:v{r.Version}", signer: signer);
        RecipesChanged?.Invoke(this, EventArgs.Empty);
        if (r.IsActive) await LoadActiveAsync();
    }

    /// <summary>切换当前生产配方，仅已审批的配方可激活</summary>
    public async Task ActivateAsync(long recipeId, string? signer = null, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Recipe.Switch);
        var r = await db.Queryable<RecipeEntity>().InSingleAsync(recipeId);
        if (r.Status != RecipeStatus.Approved) throw new InvalidOperationException("配方未审批，不能投入生产");
        var old = _current?.Code;
        await db.Updateable<RecipeEntity>().SetColumns(x => x.IsActive == false).Where(x => x.IsActive).ExecuteCommandAsync();
        await db.Updateable<RecipeEntity>().SetColumns(x => x.IsActive == true).Where(x => x.Id == recipeId).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.RecipeSwitch, "recipe", old, $"{r.Code}:v{r.Version}", signer ?? currentUser.UserName);
        await LoadActiveAsync();
        RecipesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>删除。</summary>
    public async Task DeleteAsync(long recipeId)
    {
        permissions.Demand(Permissions.Recipe.Edit);
        var r = await db.Queryable<RecipeEntity>().InSingleAsync(recipeId);
        if (r.IsActive) throw new InvalidOperationException("当前生产配方不能删除");
        await db.Deleteable<GradingRuleEntity>().Where(g => g.RecipeId == recipeId).ExecuteCommandAsync();
        await db.Deleteable<RecipeEntity>().In(recipeId).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.RecipeChange, $"recipe:{r.Code}", "存在", "已删除");
        RecipesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>模板导入使用：直接写入为已审批状态</summary>
    internal async Task ImportApprovedAsync(RecipeEntity recipe, IReadOnlyList<GradingRuleEntity> rules, bool activate)
    {
        var existing = await db.Queryable<RecipeEntity>().FirstAsync(r => r.Code == recipe.Code);
        if (existing != null) recipe.Id = existing.Id;
        await SaveAsync(recipe, rules, checkPermission: false);
        await db.Updateable<RecipeEntity>()
            .SetColumns(r => new RecipeEntity { Status = RecipeStatus.Approved, ApprovedBy = "template", ApprovedAt = DateTime.Now })
            .Where(r => r.Id == recipe.Id).ExecuteCommandAsync();
        if (activate) await ActivateAsync(recipe.Id, "template", checkPermission: false);
    }

    /// <summary>转换为双。</summary>
    internal static double? ToDouble(object? v) =>
        v switch
        {
            null => null,
            string s when string.IsNullOrWhiteSpace(s) => null,
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
            IConvertible c => Convert.ToDouble(c, CultureInfo.InvariantCulture),
            _ => null
        };
}
