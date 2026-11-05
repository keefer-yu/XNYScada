using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace XNY.Scada.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddScadaData(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.AddSingleton(options);
        services.AddSingleton<ISqlSugarClient>(sp => CreateClient(options, sp.GetService<ILogger<SqlSugarScope>>()));
        services.AddSingleton(typeof(Repository<>));
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }

    public static SqlSugarScope CreateClient(DatabaseOptions options, ILogger? logger = null)
    {
        var dbType = Enum.Parse<DbType>(options.DbType, ignoreCase: true);
        var connectionString = options.ConnectionString;
        if (dbType == DbType.Sqlite) connectionString = EnsureSqliteDirectory(connectionString);

        var nullability = new NullabilityInfoContext();
        var config = new ConnectionConfig
        {
            DbType = dbType,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (column.IsPrimarykey)
                    {
                        if (column.IsIdentity && dbType == DbType.Sqlite) column.DataType = "integer";
                        return;
                    }
                    var type = property.PropertyType;
                    if (Nullable.GetUnderlyingType(type) != null)
                    {
                        column.IsNullable = true;
                    }
                    else if (!type.IsValueType)
                    {
                        lock (nullability)
                        {
                            column.IsNullable = nullability.Create(property).WriteState == NullabilityState.Nullable;
                        }
                    }
                }
            }
        };

        return new SqlSugarScope(config, db =>
        {
            if (options.LogSql && logger != null)
            {
                db.Aop.OnLogExecuting = (sql, _) => logger.LogDebug("SQL: {Sql}", sql);
            }
            db.Aop.OnError = ex => logger?.LogError(ex, "SQL 执行失败: {Sql}", ex.Sql);
        });
    }

    private static string EnsureSqliteDirectory(string connectionString)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        if (builder.TryGetValue("Data Source", out var ds) && ds is string path && path != ":memory:")
        {
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, path);
                builder["Data Source"] = path;
            }
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        return builder.ConnectionString;
    }
}
