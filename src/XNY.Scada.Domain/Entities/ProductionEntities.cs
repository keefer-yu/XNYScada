using SqlSugar;

namespace XNY.Scada.Domain.Entities;

/// <summary>配方实体，对应表 prd_recipe。</summary>
[SugarTable("prd_recipe")]
public class RecipeEntity : EntityBase
{
    /// <summary>配方编码。</summary>
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    /// <summary>配方名称。</summary>
    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    /// <summary>产品型号。</summary>
    [SugarColumn(Length = 64)]
    public string ProductModel { get; set; } = "";

    /// <summary>版本号。</summary>
    public int Version { get; set; } = 1;

    /// <summary>配方状态。</summary>
    public RecipeStatus Status { get; set; }

    /// <summary>当前生产使用的配方</summary>
    public bool IsActive { get; set; }

    /// <summary>参数 JSON：{ "MaxIR": 0.5, "NgBoxMap": {...} }</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string ParamsJson { get; set; } = "{}";

    /// <summary>审批人。</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>审批时间。</summary>
    public DateTime? ApprovedAt { get; set; }
}

/// <summary>分档规则实体，对应表 prd_grading_rule。</summary>
[SugarTable("prd_grading_rule")]
public class GradingRuleEntity : EntityBase
{
    /// <summary>配方标识。</summary>
    public long RecipeId { get; set; }

    /// <summary>档位号。</summary>
    public int BinNo { get; set; }

    /// <summary>档位名称。</summary>
    [SugarColumn(Length = 64)]
    public string BinName { get; set; } = "";

    /// <summary>优先级，越小越先匹配</summary>
    public int Priority { get; set; }

    /// <summary>区间条件 JSON：[{ "Field":"Voltage", "Min":3.2, "Max":3.3 }]</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string ConditionsJson { get; set; } = "[]";

    /// <summary>附加表达式（NCalc），为空表示不判断</summary>
    [SugarColumn(Length = 1024)]
    public string? Expression { get; set; }

    /// <summary>满箱数量，0 表示不提醒</summary>
    public int BoxCapacity { get; set; }

    /// <summary>是否判定为 NG。</summary>
    public bool IsNg { get; set; }

    /// <summary>NG 代码。</summary>
    [SugarColumn(Length = 32)]
    public string? NgCode { get; set; }
}

/// <summary>托盘实体，对应表 prd_tray。</summary>
[SugarTable("prd_tray")]
public class TrayEntity : EntityBase
{
    /// <summary>托盘码。</summary>
    [SugarColumn(Length = 64)]
    public string TrayCode { get; set; } = "";

    /// <summary>托盘状态。</summary>
    public TrayStatus Status { get; set; }

    /// <summary>行数。</summary>
    public int Rows { get; set; }

    /// <summary>列数。</summary>
    public int Cols { get; set; }

    /// <summary>电芯数量。</summary>
    public int CellCount { get; set; }

    /// <summary>完成时间。</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>托盘单元格实体，对应表 prd_tray_cell。</summary>
[SugarTable("prd_tray_cell")]
public class TrayCellEntity : EntityBase
{
    /// <summary>托盘码。</summary>
    [SugarColumn(Length = 64)]
    public string TrayCode { get; set; } = "";

    /// <summary>托盘内位置，从 1 开始</summary>
    public int Position { get; set; }

    /// <summary>电芯条码。</summary>
    [SugarColumn(Length = 64)]
    public string CellCode { get; set; } = "";

    /// <summary>绑定时间。</summary>
    public DateTime BindTime { get; set; } = DateTime.Now;

    /// <summary>解绑时间。</summary>
    public DateTime? UnbindTime { get; set; }

    /// <summary>是否绑定是否仍然有效。</summary>
    public bool Active { get; set; } = true;
}

/// <summary>电芯生产记录，按月分表</summary>
[SugarTable("prd_cell_record_{year}{month}{day}")]
[SplitTable(SplitType.Month)]
public class CellRecordEntity
{
    /// <summary>单元格记录标识。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>电芯条码。</summary>
    [SugarColumn(Length = 64)]
    public string CellCode { get; set; } = "";

    /// <summary>托盘码。</summary>
    [SugarColumn(Length = 64)]
    public string? TrayCode { get; set; }

    /// <summary>位置。</summary>
    public int? Position { get; set; }

    /// <summary>电压。</summary>
    public double? Voltage { get; set; }

    /// <summary>内阻。</summary>
    public double? InternalResistance { get; set; }

    /// <summary>容量。</summary>
    public double? Capacity { get; set; }

    /// <summary>档位号。</summary>
    public int? BinNo { get; set; }

    /// <summary>档位名称。</summary>
    [SugarColumn(Length = 64)]
    public string? BinName { get; set; }

    /// <summary>是否合格。</summary>
    public bool IsOk { get; set; }

    /// <summary>NG 代码。</summary>
    [SugarColumn(Length = 32)]
    public string? NgCode { get; set; }

    /// <summary>工位。</summary>
    [SugarColumn(Length = 64)]
    public string? Station { get; set; }

    /// <summary>配方编码。</summary>
    [SugarColumn(Length = 64)]
    public string? RecipeCode { get; set; }

    /// <summary>机型编码。</summary>
    [SugarColumn(Length = 64)]
    public string? MachineType { get; set; }

    /// <summary>操作员。</summary>
    [SugarColumn(Length = 64)]
    public string? Operator { get; set; }

    /// <summary>扩展字段 JSON</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? ExtraJson { get; set; }

    /// <summary>创建时间。</summary>
    [SplitField]
    public DateTime CreateTime { get; set; } = DateTime.Now;
}

/// <summary>NG 记录，按月分表</summary>
[SugarTable("prd_ng_record_{year}{month}{day}")]
[SplitTable(SplitType.Month)]
public class NgRecordEntity
{
    /// <summary>NG记录标识。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>电芯条码。</summary>
    [SugarColumn(Length = 64)]
    public string CellCode { get; set; } = "";

    /// <summary>NG 代码。</summary>
    [SugarColumn(Length = 32)]
    public string NgCode { get; set; } = "";

    /// <summary>NG 原因。</summary>
    [SugarColumn(Length = 256)]
    public string? NgReason { get; set; }

    /// <summary>NG 料盒号。</summary>
    public int BoxNo { get; set; }

    /// <summary>工位。</summary>
    [SugarColumn(Length = 64)]
    public string? Station { get; set; }

    /// <summary>创建时间。</summary>
    [SplitField]
    public DateTime CreateTime { get; set; } = DateTime.Now;
}
