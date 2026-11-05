using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XNY.Scada.Application.Hosting;
using XNY.Scada.Device;
using XNY.Scada.Security;

namespace XNY.Scada.Tests;

/// <summary>基于临时 SQLite 与模拟 PLC 的完整运行时</summary>
public sealed class TestHost : IAsyncDisposable
{
    /// <summary>目录。</summary>
    private readonly string _dir;

    /// <summary>创建测试宿主。</summary>
    private TestHost(IHost host, string dir)
    {
        Host = host;
        _dir = dir;
    }

    /// <summary>宿主。</summary>
    public IHost Host { get; }

    /// <summary>服务。</summary>
    public IServiceProvider Services => Host.Services;

    /// <summary>获取。</summary>
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>启动。</summary>
    public static async Task<TestHost> StartAsync(bool login = true, Action<IServiceCollection>? configure = null, bool autoImportTemplate = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "xnyscada-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = $"Data Source={Path.Combine(dir, "test.db")}",
                ["Mes:OutboxIntervalSeconds"] = "3600",
                ["Logging:LogLevel:Default"] = "Warning"
            }))
            .ConfigureServices((ctx, s) =>
            {
                var options = new ScadaHostOptions { AutoImportTemplate = autoImportTemplate };
                options.PermissionAssemblies.Add(typeof(XNY.Scada.Presentation.PresentationServiceCollectionExtensions).Assembly);
                s.AddScadaCore(ctx.Configuration, options);
                configure?.Invoke(s);
            });
        var host = builder.Build();
        await host.StartAsync();
        var th = new TestHost(host, dir);
        if (login)
        {
            var r = await th.Get<IAuthService>().LoginAsync("admin", "Admin@123");
            if (!r.Success) throw new InvalidOperationException(r.Message);
        }
        return th;
    }

    /// <summary>等待截止。</summary>
    public async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    /// <summary>标记。</summary>
    public object? Tag(string name) => Get<ITagService>().GetValue(name)?.Value;

    /// <summary>释放资源。</summary>
    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
