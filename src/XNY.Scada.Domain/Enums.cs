namespace XNY.Scada.Domain;

public enum TagDataType
{
    Bool,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    Float,
    Double,
    String
}

public enum TagAccess
{
    Read,
    Write,
    ReadWrite
}

public enum AlarmKind
{
    /// <summary>位报警：值为 true 时报警</summary>
    Bit,
    High,
    Low,
    Equal
}

public enum AlarmLevel
{
    Info,
    Warning,
    Error,
    Critical
}

public enum PermissionKind
{
    Menu,
    Page,
    Action,
    Data
}

public enum FlowVersionStatus
{
    Draft,
    Published,
    Archived
}

public enum FlowInstanceStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

public enum RecipeStatus
{
    Draft,
    PendingApproval,
    Approved,
    Archived
}

public enum TrayStatus
{
    Loading,
    Full,
    Uploaded,
    Unloading,
    Empty
}

public enum MesOutboxStatus
{
    Pending,
    Sent,
    Failed
}

public static class MachineTypes
{
    public const string TrayAssembly = "TrayAssembly";
    public const string TrayDisassembly = "TrayDisassembly";
    public const string Grading = "Grading";
    public const string NgSorting = "NgSorting";

    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        [TrayAssembly] = "组盘机",
        [TrayDisassembly] = "拆盘机",
        [Grading] = "分档机",
        [NgSorting] = "NG挑选机"
    };
}
