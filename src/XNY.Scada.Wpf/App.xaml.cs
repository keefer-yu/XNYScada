using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XNY.Scada.Application.Hosting;
using XNY.Scada.Presentation;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.Wpf.Services;

namespace XNY.Scada.Wpf;

public partial class App
{
    private IHost? _host;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Global\\XNY.Scada.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("SCADA 已在运行（同一台机器只允许一个实例访问 PLC）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception, "未处理异常");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception, "未观察的任务异常");
            args.SetObserved();
        };

        var splash = new SplashWindow();
        splash.Show();
        EventHandler<string> progress = (_, msg) => Dispatcher.BeginInvoke(() => splash.Message = msg);
        ScadaRuntimeService.StartupProgress += progress;
        try
        {
            _host = ScadaHost.CreateBuilder(e.Args, (services, _) =>
            {
                services.AddScadaPresentation();
                services.AddSingleton<IUiDispatcher>(new WpfDispatcher(Dispatcher));
                services.AddSingleton<IDialogService, WpfDialogService>();
                services.AddSingleton<MainWindow>();
            }, PresentationServiceCollectionExtensions.CreateHostOptions()).Build();

            await Task.Run(() => _host.StartAsync());

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Closed += async (_, _) => await ShutdownHostAsync();
            window.Show();
        }
        catch (Exception ex)
        {
            Log(ex, "启动失败");
            MessageBox.Show($"系统启动失败：{ex.Message}\n\n详细信息见 logs 目录。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            await ShutdownHostAsync();
        }
        finally
        {
            ScadaRuntimeService.StartupProgress -= progress;
            splash.Close();
        }
    }

    private async Task ShutdownHostAsync()
    {
        if (_host != null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Log(ex, "停止失败");
            }
            _host.Dispose();
            _host = null;
        }
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        _singleInstance = null;
        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception, "界面异常");
        MessageBox.Show(e.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void Log(Exception? ex, string message)
    {
        var logger = _host?.Services.GetService<ILoggerFactory>()?.CreateLogger("App");
        if (logger != null) logger.LogError(ex, "{Message}", message);
        else Serilog.Log.Error(ex, message);
    }
}
