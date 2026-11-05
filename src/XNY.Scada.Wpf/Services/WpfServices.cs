using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Wpf.Views.Dialogs;

namespace XNY.Scada.Wpf.Services;

public sealed class WpfDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Invoke(Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    public void BeginInvoke(Action action) => dispatcher.BeginInvoke(action);
}

public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? System.Windows.Application.Current?.MainWindow;

    private static T OnUi<T>(Func<T> func)
    {
        var d = System.Windows.Application.Current.Dispatcher;
        return d.CheckAccess() ? func() : d.Invoke(func);
    }

    private static MessageBoxResult Box(string message, string title, MessageBoxButton button, MessageBoxImage image) =>
        OnUi(() => Owner is { } owner
            ? MessageBox.Show(owner, message, title, button, image)
            : MessageBox.Show(message, title, button, image));

    public void ShowInfo(string message, string title = "提示") => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message, string title = "错误") => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "确认") =>
        Box(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? Prompt(string message, string title, string defaultValue = "") => OnUi(() =>
    {
        var dlg = new PromptDialog(title, message, defaultValue) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    });

    public SignatureCredential? RequestSignature(string reason) => OnUi(() =>
    {
        var dlg = new SignatureDialog(reason) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    });

    public PasswordChange? RequestPasswordChange() => OnUi(() =>
    {
        var dlg = new ChangePasswordDialog { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    });

    public string? OpenFile(string filter) => OnUi(() =>
    {
        var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    });

    public string? SaveFile(string filter, string defaultName) => OnUi(() =>
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    });
}
