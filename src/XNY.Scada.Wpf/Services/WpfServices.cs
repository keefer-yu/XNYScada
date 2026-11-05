using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Wpf.Views.Dialogs;

namespace XNY.Scada.Wpf.Services;

/// <summary>WPF调度器。</summary>
/// <param name="dispatcher">调度器。</param>
public sealed class WpfDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    /// <summary>检查访问。</summary>
    public bool CheckAccess() => dispatcher.CheckAccess();

    /// <summary>调用。</summary>
    public void Invoke(Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    /// <summary>开始调用。</summary>
    public void BeginInvoke(Action action) => dispatcher.BeginInvoke(action);
}

/// <summary>WPF对话框服务。</summary>
public sealed class WpfDialogService : IDialogService
{
    /// <summary>所有者。</summary>
    private static Window? Owner => System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? System.Windows.Application.Current?.MainWindow;

    /// <summary>处理界面。</summary>
    private static T OnUi<T>(Func<T> func)
    {
        var d = System.Windows.Application.Current.Dispatcher;
        return d.CheckAccess() ? func() : d.Invoke(func);
    }

    /// <summary>装箱。</summary>
    private static MessageBoxResult Box(string message, string title, MessageBoxButton button, MessageBoxImage image) =>
        OnUi(() => Owner is { } owner
            ? MessageBox.Show(owner, message, title, button, image)
            : MessageBox.Show(message, title, button, image));

    /// <summary>显示信息。</summary>
    public void ShowInfo(string message, string title = "提示") => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>显示错误。</summary>
    public void ShowError(string message, string title = "错误") => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>确认。</summary>
    public bool Confirm(string message, string title = "确认") =>
        Box(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>提示。</summary>
    public string? Prompt(string message, string title, string defaultValue = "") => OnUi(() =>
    {
        var dlg = new PromptDialog(title, message, defaultValue) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    });

    /// <summary>请求签名。</summary>
    public SignatureCredential? RequestSignature(string reason) => OnUi(() =>
    {
        var dlg = new SignatureDialog(reason) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    });

    /// <summary>请求密码更改。</summary>
    public PasswordChange? RequestPasswordChange() => OnUi(() =>
    {
        var dlg = new ChangePasswordDialog { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    });

    /// <summary>打开文件。</summary>
    public string? OpenFile(string filter) => OnUi(() =>
    {
        var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    });

    /// <summary>保存文件。</summary>
    public string? SaveFile(string filter, string defaultName) => OnUi(() =>
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    });
}
