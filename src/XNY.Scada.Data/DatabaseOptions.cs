namespace XNY.Scada.Data;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Sqlite / SqlServer / MySql / PostgreSQL</summary>
    public string DbType { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=data/scada.db";

    /// <summary>生产数据保留月数，0 表示不清理</summary>
    public int RetentionMonths { get; set; } = 12;

    public bool LogSql { get; set; }
}
