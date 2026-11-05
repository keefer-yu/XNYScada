namespace XNY.Scada.Data;

/// <summary>数据库配置，对应 appsettings.json 的 Database 节。由 SqlSugar 按 DbType 切换方言。</summary>
public sealed class DatabaseOptions
{
    /// <summary>配置节名称。</summary>
    public const string Section = "Database";

    /// <summary>Sqlite / SqlServer / MySql / PostgreSQL</summary>
    public string DbType { get; set; } = "Sqlite";

    /// <summary>连接字符串。</summary>
    public string ConnectionString { get; set; } = "Data Source=data/scada.db";

    /// <summary>生产数据保留月数，0 表示不清理</summary>
    public int RetentionMonths { get; set; } = 12;

    /// <summary>是否记录 SQL。</summary>
    public bool LogSql { get; set; }
}
