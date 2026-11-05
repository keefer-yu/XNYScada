using SqlSugar;

namespace XNY.Scada.Domain.Entities;

[SugarTable("prd_recipe")]
public class RecipeEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string ProductModel { get; set; } = "";

    public int Version { get; set; } = 1;

    public RecipeStatus Status { get; set; }

    /// <summary>当前生产使用的配方</summary>
    public bool IsActive { get; set; }

    /// <summary>参数 JSON：{ "MaxIR": 0.5, "NgBoxMap": {...} }</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string ParamsJson { get; set; } = "{}";

    public string? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }
}

[SugarTable("prd_grading_rule")]
public class GradingRuleEntity : EntityBase
{
    public long RecipeId { get; set; }

    public int BinNo { get; set; }

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

    public bool IsNg { get; set; }

    [SugarColumn(Length = 32)]
    public string? NgCode { get; set; }
}

[SugarTable("prd_tray")]
public class TrayEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string TrayCode { get; set; } = "";

    public TrayStatus Status { get; set; }

    public int Rows { get; set; }

    public int Cols { get; set; }

    public int CellCount { get; set; }

    public DateTime? CompletedAt { get; set; }
}

[SugarTable("prd_tray_cell")]
public class TrayCellEntity : EntityBase
{
    [SugarColumn(Length = 64)]
    public string TrayCode { get; set; } = "";

    /// <summary>托盘内位置，从 1 开始</summary>
    public int Position { get; set; }

    [SugarColumn(Length = 64)]
    public string CellCode { get; set; } = "";

    public DateTime BindTime { get; set; } = DateTime.Now;

    public DateTime? UnbindTime { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>电芯生产记录，按月分表</summary>
[SugarTable("prd_cell_record_{year}{month}{day}")]
[SplitTable(SplitType.Month)]
public class CellRecordEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string CellCode { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string? TrayCode { get; set; }

    public int? Position { get; set; }

    public double? Voltage { get; set; }

    public double? InternalResistance { get; set; }

    public double? Capacity { get; set; }

    public int? BinNo { get; set; }

    [SugarColumn(Length = 64)]
    public string? BinName { get; set; }

    public bool IsOk { get; set; }

    [SugarColumn(Length = 32)]
    public string? NgCode { get; set; }

    [SugarColumn(Length = 64)]
    public string? Station { get; set; }

    [SugarColumn(Length = 64)]
    public string? RecipeCode { get; set; }

    [SugarColumn(Length = 64)]
    public string? MachineType { get; set; }

    [SugarColumn(Length = 64)]
    public string? Operator { get; set; }

    /// <summary>扩展字段 JSON</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string? ExtraJson { get; set; }

    [SplitField]
    public DateTime CreateTime { get; set; } = DateTime.Now;
}

/// <summary>NG 记录，按月分表</summary>
[SugarTable("prd_ng_record_{year}{month}{day}")]
[SplitTable(SplitType.Month)]
public class NgRecordEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string CellCode { get; set; } = "";

    [SugarColumn(Length = 32)]
    public string NgCode { get; set; } = "";

    [SugarColumn(Length = 256)]
    public string? NgReason { get; set; }

    public int BoxNo { get; set; }

    [SugarColumn(Length = 64)]
    public string? Station { get; set; }

    [SplitField]
    public DateTime CreateTime { get; set; } = DateTime.Now;
}
