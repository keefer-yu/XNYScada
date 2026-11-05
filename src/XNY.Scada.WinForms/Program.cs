using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XNY.Scada.Application.Hosting;
using XNY.Scada.Presentation;
using XNY.Scada.Presentation.Services;
using XNY.Scada.WinForms.Forms;
using XNY.Scada.WinForms.Mvvm;
using XNY.Scada.WinForms.Services;
using WinApp = System.Windows.Forms.Application;

namespace XNY.Scada.WinForms;

internal static class Program
{
    private static IHost? _host;

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, "Global\\XNY.Scada.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("SCADA 已在运行（同一台机器只允许一个实例访问 PLC）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        WinApp.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        WinApp.ThreadException += (_, e) =>
        {
            Log(e.Exception, "界面异常");
            MessageBox.Show(e.Exception.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception, "未处理异常");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(e.Exception, "未观察的任务异常");
            e.SetObserved();
        };

        var dispatcher = new WinFormsDispatcher();
        Bind.Dispatcher = dispatcher;

        MainForm? main;
        using (var splash = new SplashForm())
        {
            splash.Show();
            EventHandler<string> progress = (_, msg) => dispatcher.BeginInvoke(() => splash.Message = msg);
            ScadaRuntimeService.StartupProgress += progress;
            try
            {
                _host = ScadaHost.CreateBuilder(args, (services, _) =>
                {
                    services.AddScadaPresentation();
                    services.AddSingleton<IUiDispatcher>(dispatcher);
                    services.AddSingleton<IDialogService, WinFormsDialogService>();
                    services.AddSingleton<MainForm>();
                }, PresentationServiceCollectionExtensions.CreateHostOptions()).Build();

                var start = Task.Run(() => _host.StartAsync());
                while (!start.IsCompleted)
                {
                    WinApp.DoEvents();
                    Thread.Sleep(15);
                }
                start.GetAwaiter().GetResult();
                main = _host.Services.GetRequiredService<MainForm>();
            }
            catch (Exception ex)
            {
                Log(ex, "启动失败");
                MessageBox.Show($"系统启动失败：{ex.Message}\n\n详细信息见 logs 目录。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopHost();
                return;
            }
            finally
            {
                ScadaRuntimeService.StartupProgress -= progress;
            }
        }

        WinApp.Run(main);
        StopHost();
    }

    private static void StopHost()
    {
        if (_host == null) return;
        try
        {
            Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log(ex, "停止失败");
        }
        _host.Dispose();
        _host = null;
    }

    private static void Log(Exception? ex, string message)
    {
        var logger = _host?.Services.GetService<ILoggerFactory>()?.CreateLogger("App");
        if (logger != null) logger.LogError(ex, "{Message}", message);
        else Serilog.Log.Error(ex, message);
    }
}
