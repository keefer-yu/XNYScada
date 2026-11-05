using XNY.Scada.Presentation.Services;
using XNY.Scada.WinForms.Forms;

namespace XNY.Scada.WinForms.Services;

/// <summary>基于 UI 线程同步上下文的调度器（需在 UI 线程创建）</summary>
public sealed class WinFormsDispatcher : IUiDispatcher
{
    /// <summary>封送。</summary>
    private readonly Control _marshal;

    /// <summary>创建Windows窗体调度器。</summary>
    public WinFormsDispatcher()
    {
        _marshal = new Control();
        _marshal.CreateControl();
        _ = _marshal.Handle;
    }

    /// <summary>检查访问。</summary>
    public bool CheckAccess() => !_marshal.InvokeRequired;

    /// <summary>调用。</summary>
    public void Invoke(Action action)
    {
        if (CheckAccess()) action();
        else _marshal.Invoke(action);
    }

    /// <summary>开始调用。</summary>
    public void BeginInvoke(Action action)
    {
        if (_marshal.IsDisposed) return;
        _marshal.BeginInvoke(action);
    }

    /// <summary>调用。</summary>
    public T Invoke<T>(Func<T> func) => CheckAccess() ? func() : (T)_marshal.Invoke(func);
}

/// <summary>Windows窗体对话框服务。</summary>
/// <param name="ui">界面。</param>
public sealed class WinFormsDialogService(IUiDispatcher ui) : IDialogService
{
    /// <summary>所有者。</summary>
    private static IWin32Window? Owner => Form.ActiveForm ?? System.Windows.Forms.Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Visible);

    /// <summary>处理界面。</summary>
    private T OnUi<T>(Func<T> func) => ui is WinFormsDispatcher d ? d.Invoke(func) : func();

    /// <summary>装箱。</summary>
    private DialogResult Box(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        OnUi(() => Owner is { } owner ? MessageBox.Show(owner, message, title, buttons, icon) : MessageBox.Show(message, title, buttons, icon));

    /// <summary>显示信息。</summary>
    public void ShowInfo(string message, string title = "提示") => Box(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>显示错误。</summary>
    public void ShowError(string message, string title = "错误") => Box(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>确认。</summary>
    public bool Confirm(string message, string title = "确认") =>
        Box(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>提示。</summary>
    public string? Prompt(string message, string title, string defaultValue = "") => OnUi(() =>
    {
        using var dlg = new PromptForm(title, message, defaultValue);
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.Value : null;
    });

    /// <summary>请求签名。</summary>
    public SignatureCredential? RequestSignature(string reason) => OnUi(() =>
    {
        using var dlg = new SignatureForm(reason);
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.Result : null;
    });

    /// <summary>请求密码更改。</summary>
    public PasswordChange? RequestPasswordChange() => OnUi(() =>
    {
        using var dlg = new ChangePasswordForm();
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.Result : null;
    });

    /// <summary>打开文件。</summary>
    public string? OpenFile(string filter) => OnUi(() =>
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.FileName : null;
    });

    /// <summary>保存文件。</summary>
    public string? SaveFile(string filter, string defaultName) => OnUi(() =>
    {
        using var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName };
        return dlg.ShowDialog(Owner) == DialogResult.OK ? dlg.FileName : null;
    });
}
