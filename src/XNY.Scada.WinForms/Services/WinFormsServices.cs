using XNY.Scada.Presentation.Services;
using XNY.Scada.WinForms.Forms;

namespace XNY.Scada.WinForms.Services;

/// <summary>基于 UI 线程同步上下文的调度器（需在 UI 线程创建）</summary>
public sealed class WinFormsDispatcher : IUiDispatcher
{
    private readonly Control _marshal;

    public WinFormsDispatcher()
    {
        _marshal = new Control();
        _marshal.CreateControl();
        _ = _marshal.Handle;
    }

    public bool CheckAccess() => !_marshal.InvokeRequired;

    public void Invoke(Action action)
    {
        if (CheckAccess()) action();
        else _marshal.Invoke(action);
    }

    public void BeginInvoke(Action action)
    {
        if (_marshal.IsDisposed) return;
        _marshal.BeginInvoke(action);
    }

    public T Invoke<T>(Func<T> func) => CheckAccess() ? func() : (T)_marshal.Invoke(func);
}

public sealed class WinFormsDialogService(IUiDispatcher ui) : IDialogService
{
    private static IWin32Window? Owner => Form.ActiveForm ?? System.Windows.Forms.Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Visible);

    private T OnUi<T>(Func<T> func) => ui is WinFormsDispatcher d ? d.Invoke(func) : func();

    private DialogResult Box(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        OnUi(() => Owner is { } owner ? MessageBox.Show(owner, message, title, buttons, icon) : MessageBox.Show(message, title, buttons, icon));

    public void ShowInfo(string message, string title = "提示") => Box(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public void ShowError(string message, string title = "错误") => Box(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public bool Confirm(string message, string title = "确认") =>
        Box(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public string? Prompt(string message, string title, string defaultValue = "") => OnUi(() =>
    {
        using var dlg = new PromptForm(title, message, defaultValue);
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.Value : null;
    });

    public SignatureCredential? RequestSignature(string reason) => OnUi(() =>
    {
        using var dlg = new SignatureForm(reason);
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.Result : null;
    });

    public PasswordChange? RequestPasswordChange() => OnUi(() =>
    {
        using var dlg = new ChangePasswordForm();
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.Result : null;
    });

    public string? OpenFile(string filter) => OnUi(() =>
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.FileName : null;
    });

    public string? SaveFile(string filter, string defaultName) => OnUi(() =>
    {
        using var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName };
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.FileName : null;
    });
}
