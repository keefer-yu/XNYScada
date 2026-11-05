namespace XNY.Scada.Domain;

/// <summary>点位在 PLC 中的数据类型。字符串需同时配置长度。</summary>
public enum TagDataType
{
    /// <summary>布尔。</summary>
    Bool,
    /// <summary>16 位有符号整数。</summary>
    Int16,
    /// <summary>16 位无符号整数。</summary>
    UInt16,
    /// <summary>32 位有符号整数。</summary>
    Int32,
    /// <summary>32 位无符号整数。</summary>
    UInt32,
    /// <summary>64 位有符号整数。</summary>
    Int64,
    /// <summary>单精度浮点数。</summary>
    Float,
    /// <summary>双精度浮点数。</summary>
    Double,
    /// <summary>字符串。</summary>
    String
}

/// <summary>点位相对上位机的访问方向。</summary>
public enum TagAccess
{
    /// <summary>只读。</summary>
    Read,
    /// <summary>只写。</summary>
    Write,
    /// <summary>可读可写。</summary>
    ReadWrite
}

/// <summary>报警类别。</summary>
public enum AlarmKind
{
    /// <summary>位报警：值为 true 时报警</summary>
    Bit,
    /// <summary>高限报警。</summary>
    High,
    /// <summary>低限报警。</summary>
    Low,
    /// <summary>等值报警。</summary>
    Equal
}

/// <summary>报警严重程度，界面按级别区分提示。</summary>
public enum AlarmLevel
{
    /// <summary>信息。</summary>
    Info,
    /// <summary>警告。</summary>
    Warning,
    /// <summary>错误。</summary>
    Error,
    /// <summary>严重。</summary>
    Critical
}

/// <summary>权限点类别：菜单、页面、操作或数据范围。</summary>
public enum PermissionKind
{
    /// <summary>菜单。</summary>
    Menu,
    /// <summary>页面。</summary>
    Page,
    /// <summary>操作。</summary>
    Action,
    /// <summary>数据范围。</summary>
    Data
}

/// <summary>流程版本状态。已发布版本不可再改，变更需新建版本。</summary>
public enum FlowVersionStatus
{
    /// <summary>草稿。</summary>
    Draft,
    /// <summary>已发布。</summary>
    Published,
    /// <summary>已归档。</summary>
    Archived
}

/// <summary>一次流程运行实例的结果。</summary>
public enum FlowInstanceStatus
{
    /// <summary>运行中。</summary>
    Running,
    /// <summary>已完成。</summary>
    Completed,
    /// <summary>失败。</summary>
    Failed,
    /// <summary>已取消。</summary>
    Cancelled,
    /// <summary>已超时。</summary>
    TimedOut
}

/// <summary>配方生命周期。只有已审批配方可以切换为当前配方。</summary>
public enum RecipeStatus
{
    /// <summary>草稿。</summary>
    Draft,
    /// <summary>待审批。</summary>
    PendingApproval,
    /// <summary>已审批。</summary>
    Approved,
    /// <summary>已归档。</summary>
    Archived
}

/// <summary>托盘在组盘/拆盘过程中的状态。</summary>
public enum TrayStatus
{
    /// <summary>组盘中。</summary>
    Loading,
    /// <summary>已满盘。</summary>
    Full,
    /// <summary>已上传。</summary>
    Uploaded,
    /// <summary>拆盘中。</summary>
    Unloading,
    /// <summary>空盘。</summary>
    Empty
}

/// <summary>MES 离线发件箱中一条待补传记录的状态。</summary>
public enum MesOutboxStatus
{
    /// <summary>待发送。</summary>
    Pending,
    /// <summary>已发送。</summary>
    Sent,
    /// <summary>发送失败。</summary>
    Failed
}

/// <summary>内置机型编码。界面显示名见 <see cref="DisplayNames"/>。</summary>
public static class MachineTypes
{
    /// <summary>组盘机。</summary>
    public const string TrayAssembly = "TrayAssembly";
    /// <summary>拆盘机。</summary>
    public const string TrayDisassembly = "TrayDisassembly";
    /// <summary>分档机。</summary>
    public const string Grading = "Grading";
    /// <summary>NG 挑选机。</summary>
    public const string NgSorting = "NgSorting";

    /// <summary>机型编码与中文名的对照。</summary>
    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        [TrayAssembly] = "组盘机",
        [TrayDisassembly] = "拆盘机",
        [Grading] = "分档机",
        [NgSorting] = "NG挑选机"
    };
}
