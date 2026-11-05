using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Application.Production;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>分档规则行，条件以文本编辑：Voltage:3.2~3.3; InternalResistance:~0.5</summary>
public sealed partial class GradingRuleRow : ObservableObject
{
    /// <summary>档位不。</summary>
    [ObservableProperty] private int _binNo;
    /// <summary>档位名称。</summary>
    [ObservableProperty] private string _binName = "";
    /// <summary>优先级。</summary>
    [ObservableProperty] private int _priority;
    /// <summary>条件。</summary>
    [ObservableProperty] private string _conditions = "";
    /// <summary>表达式。</summary>
    [ObservableProperty] private string? _expression;
    /// <summary>料盒容量。</summary>
    [ObservableProperty] private int _boxCapacity;
    /// <summary>是否NG。</summary>
    [ObservableProperty] private bool _isNg;
    /// <summary>NG代码。</summary>
    [ObservableProperty] private string? _ngCode;

    /// <summary>起点节点。</summary>
    public static GradingRuleRow From(GradingRuleEntity e) => new()
    {
        BinNo = e.BinNo,
        BinName = e.BinName,
        Priority = e.Priority,
        Conditions = FormatConditions(Json.Deserialize<List<GradingCondition>>(e.ConditionsJson) ?? []),
        Expression = e.Expression,
        BoxCapacity = e.BoxCapacity,
        IsNg = e.IsNg,
        NgCode = e.NgCode
    };

    /// <summary>转换为实体。</summary>
    public GradingRuleEntity ToEntity() => new()
    {
        BinNo = BinNo,
        BinName = BinName,
        Priority = Priority,
        ConditionsJson = Json.Serialize(ParseConditions(Conditions)),
        Expression = string.IsNullOrWhiteSpace(Expression) ? null : Expression,
        BoxCapacity = BoxCapacity,
        IsNg = IsNg,
        NgCode = string.IsNullOrWhiteSpace(NgCode) ? null : NgCode
    };

    /// <summary>格式化条件。</summary>
    public static string FormatConditions(IEnumerable<GradingCondition> list) =>
        string.Join("; ", list.Select(c => $"{c.Field}:{Fmt(c.Min)}~{Fmt(c.Max)}"));

    /// <summary>格式。</summary>
    private static string Fmt(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>解析条件。</summary>
    public static List<GradingCondition> ParseConditions(string? text)
    {
        var list = new List<GradingCondition>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var part in text.Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOfAny([':', '：']);
            if (idx <= 0) throw new FormatException($"条件格式错误：{part}，应为 字段:下限~上限");
            var field = part[..idx].Trim();
            var range = part[(idx + 1)..].Split('~');
            if (range.Length != 2) throw new FormatException($"条件格式错误：{part}，应为 字段:下限~上限");
            list.Add(new GradingCondition { Field = field, Min = ParseNum(range[0], part), Max = ParseNum(range[1], part) });
        }
        return list;
    }

    /// <summary>解析编号。</summary>
    private static double? ParseNum(string s, string part)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new FormatException($"条件数值错误：{part}");
    }
}

/// <summary>配方参数行。</summary>
public sealed partial class RecipeParamRow : ObservableObject
{
    /// <summary>配方参数行名称。</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>配方参数行值。</summary>
    [ObservableProperty] private string _value = "";
}

/// <summary>配方页面的视图模型。</summary>
public sealed partial class RecipeViewModel : ViewModelBase
{
    /// <summary>服务。</summary>
    private readonly RecipeService _service;

    /// <summary>创建配方视图模型。</summary>
    public RecipeViewModel(ViewModelContext context, RecipeService service) : base(context)
    {
        _service = service;
        _service.RecipesChanged += (_, _) => Ui.BeginInvoke(() => { if (IsActive) _ = RunAsync(ReloadListAsync); });

        NewCommand = Secured(Permissions.Recipe.Edit, NewAsync);
        CopyCommand = Secured(Permissions.Recipe.Edit, CopyAsync, () => Selected != null);
        SaveCommand = Secured(Permissions.Recipe.Edit, SaveAsync, () => Selected != null);
        DeleteCommand = Secured(Permissions.Recipe.Edit, DeleteAsync, () => Selected is { Id: > 0, IsActive: false });
        SubmitCommand = Secured(Permissions.Recipe.Edit, SubmitAsync, () => Selected is { Id: > 0, Status: RecipeStatus.Draft });
        ApproveCommand = Secured(Permissions.Recipe.Approve, ApproveAsync, () => Selected is { Status: RecipeStatus.PendingApproval });
        ActivateCommand = Secured(Permissions.Recipe.Switch, ActivateRecipeAsync, () => Selected is { Status: RecipeStatus.Approved, IsActive: false });
        AddRuleCommand = Secured(Permissions.Recipe.Edit, () =>
        {
            Rules.Add(new GradingRuleRow { BinNo = Rules.Count + 1, BinName = $"档位{Rules.Count + 1}", Priority = Rules.Count + 1 });
            return Task.CompletedTask;
        });
        RemoveRuleCommand = Secured<GradingRuleRow>(Permissions.Recipe.Edit, r => { if (r != null) Rules.Remove(r); return Task.CompletedTask; });
        AddParamCommand = Secured(Permissions.Recipe.Edit, () => { Params.Add(new RecipeParamRow { Name = "NewParam" }); return Task.CompletedTask; });
        RemoveParamCommand = Secured<RecipeParamRow>(Permissions.Recipe.Edit, p => { if (p != null) Params.Remove(p); return Task.CompletedTask; });
    }

    /// <summary>标题。</summary>
    public override string Title => "配方管理";

    /// <summary>配方数量。</summary>
    public ObservableCollection<RecipeEntity> Recipes { get; } = [];
    /// <summary>规则。</summary>
    public ObservableCollection<GradingRuleRow> Rules { get; } = [];
    /// <summary>参数。</summary>
    public ObservableCollection<RecipeParamRow> Params { get; } = [];

    /// <summary>选中。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(SaveCommand), nameof(DeleteCommand), nameof(SubmitCommand), nameof(ApproveCommand), nameof(ActivateCommand))]
    private RecipeEntity? _selected;

    /// <summary>代码。</summary>
    [ObservableProperty] private string _code = "";
    /// <summary>配方名称。</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>产品型号。</summary>
    [ObservableProperty] private string _productModel = "";
    /// <summary>状态文本。</summary>
    [ObservableProperty] private string _statusText = "";
    /// <summary>当前配方。</summary>
    [ObservableProperty] private string _currentRecipe = "";

    /// <summary>新命令。</summary>
    public IAsyncRelayCommand NewCommand { get; }
    /// <summary>复制命令。</summary>
    public IAsyncRelayCommand CopyCommand { get; }
    /// <summary>保存命令。</summary>
    public IAsyncRelayCommand SaveCommand { get; }
    /// <summary>删除命令。</summary>
    public IAsyncRelayCommand DeleteCommand { get; }
    /// <summary>提交命令。</summary>
    public IAsyncRelayCommand SubmitCommand { get; }
    /// <summary>审批命令。</summary>
    public IAsyncRelayCommand ApproveCommand { get; }
    /// <summary>激活命令。</summary>
    public IAsyncRelayCommand ActivateCommand { get; }
    /// <summary>添加规则命令。</summary>
    public IAsyncRelayCommand AddRuleCommand { get; }
    /// <summary>移除规则命令。</summary>
    public IAsyncRelayCommand<GradingRuleRow> RemoveRuleCommand { get; }
    /// <summary>添加参数命令。</summary>
    public IAsyncRelayCommand AddParamCommand { get; }
    /// <summary>移除参数命令。</summary>
    public IAsyncRelayCommand<RecipeParamRow> RemoveParamCommand { get; }

    /// <summary>处理激活。</summary>
    protected override Task OnActivatedAsync() => ReloadListAsync();

    /// <summary>重新加载列表。</summary>
    private async Task ReloadListAsync()
    {
        var id = Selected?.Id;
        Replace(Recipes, await _service.ListAsync());
        CurrentRecipe = _service.Current is { } c ? $"{c.Code} {c.Name} v{c.Version}" : "无";
        Selected = Recipes.FirstOrDefault(r => r.Id == id) ?? Recipes.FirstOrDefault(r => r.IsActive) ?? Recipes.FirstOrDefault();
    }

    /// <summary>当当前选中项变化时更新关联状态。</summary>
    partial void OnSelectedChanged(RecipeEntity? value) => _ = RunAsync(() => LoadDetailAsync(value));

    /// <summary>加载详情。</summary>
    private async Task LoadDetailAsync(RecipeEntity? r)
    {
        Code = r?.Code ?? "";
        Name = r?.Name ?? "";
        ProductModel = r?.ProductModel ?? "";
        StatusText = r == null ? "" : r.Status switch
        {
            RecipeStatus.Draft => "草稿",
            RecipeStatus.PendingApproval => "待审批",
            RecipeStatus.Approved => $"已审批（{r.ApprovedBy} {r.ApprovedAt:yyyy-MM-dd HH:mm}）",
            _ => r.Status.ToString()
        } + (r.IsActive ? " · 生产中" : "");
        Replace(Params, Json.ToDictionary(r?.ParamsJson).Select(kv => new RecipeParamRow { Name = kv.Key, Value = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? "" }));
        Replace(Rules, r == null || r.Id == 0 ? [] : (await _service.GetRulesAsync(r.Id)).Select(GradingRuleRow.From));
    }

    /// <summary>新建。</summary>
    private Task NewAsync()
    {
        Selected = new RecipeEntity { Code = $"R{DateTime.Now:yyMMddHHmm}", Name = "新配方" };
        return Task.CompletedTask;
    }

    /// <summary>复制。</summary>
    private Task CopyAsync()
    {
        if (Selected == null) return Task.CompletedTask;
        var rules = Rules.ToList();
        var ps = Params.ToList();
        Selected = new RecipeEntity { Code = Selected.Code + "_COPY", Name = Selected.Name + " 副本", ProductModel = Selected.ProductModel };
        Replace(Rules, rules);
        Replace(Params, ps);
        return Task.CompletedTask;
    }

    /// <summary>构建参数JSON。</summary>
    private string BuildParamsJson()
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in Params.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            dict[p.Name.Trim()] = double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
                : bool.TryParse(p.Value, out var b) ? b : p.Value;
        }
        return Json.Serialize(dict);
    }

    /// <summary>保存。</summary>
    private async Task SaveAsync()
    {
        if (Selected == null) return;
        if (Selected.Status == RecipeStatus.Approved && !Dialogs.Confirm("修改已审批配方将生成新版本并需要重新审批，继续？")) return;
        var rules = Rules.Select(r => r.ToEntity()).ToList();
        var recipe = new RecipeEntity
        {
            Id = Selected.Id, Code = Code.Trim(), Name = Name.Trim(), ProductModel = ProductModel.Trim(),
            Version = Selected.Version, ParamsJson = BuildParamsJson(), CreatedAt = Selected.CreatedAt
        };
        var saved = await _service.SaveAsync(recipe, rules);
        Selected = saved;
        await ReloadListAsync();
        StatusMessage = "配方已保存为草稿";
    }

    /// <summary>删除。</summary>
    private async Task DeleteAsync()
    {
        if (Selected == null || !Dialogs.Confirm($"删除配方 {Selected.Code}？")) return;
        await _service.DeleteAsync(Selected.Id);
        Selected = null;
        await ReloadListAsync();
    }

    /// <summary>提交。</summary>
    private async Task SubmitAsync()
    {
        if (Selected == null) return;
        await _service.SubmitAsync(Selected.Id);
        await ReloadListAsync();
    }

    /// <summary>审批通过。</summary>
    private async Task ApproveAsync()
    {
        if (Selected == null) return;
        var signer = await SignAsync(Permissions.Recipe.Approve, $"审批配方 {Selected.Code} v{Selected.Version}");
        if (signer == null) return;
        await _service.ApproveAsync(Selected.Id, signer);
        await ReloadListAsync();
    }

    /// <summary>激活配方。</summary>
    private async Task ActivateRecipeAsync()
    {
        if (Selected == null) return;
        var signer = await SignAsync(Permissions.Recipe.Switch, $"切换生产配方为 {Selected.Code} v{Selected.Version}");
        if (signer == null) return;
        await _service.ActivateAsync(Selected.Id, signer);
        await ReloadListAsync();
    }
}
