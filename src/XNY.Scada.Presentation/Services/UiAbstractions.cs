namespace XNY.Scada.Presentation.Services;

/// <summary>签名凭据。</summary>
/// <param name="UserName">用户名。</param>
/// <param name="Password">密码。</param>
/// <param name="Comment">注释。</param>
public sealed record SignatureCredential(string UserName, string Password, string? Comment);

/// <summary>密码更改。</summary>
/// <param name="OldPassword">旧密码。</param>
/// <param name="NewPassword">新密码。</param>
public sealed record PasswordChange(string OldPassword, string NewPassword);

/// <summary>对话框抽象，WPF 与 WinForms 各自实现</summary>
public interface IDialogService
{
    /// <summary>显示信息。</summary>
    void ShowInfo(string message, string title = "提示");

    /// <summary>显示错误。</summary>
    void ShowError(string message, string title = "错误");

    /// <summary>确认。</summary>
    bool Confirm(string message, string title = "确认");

    /// <summary>提示。</summary>
    string? Prompt(string message, string title, string defaultValue = "");

    /// <summary>电子签名对话框：输入签名人账号、密码、备注</summary>
    SignatureCredential? RequestSignature(string reason);

    /// <summary>请求密码更改。</summary>
    PasswordChange? RequestPasswordChange();

    /// <summary>打开文件。</summary>
    string? OpenFile(string filter);

    /// <summary>保存文件。</summary>
    string? SaveFile(string filter, string defaultName);
}

/// <summary>UI 线程调度</summary>
public interface IUiDispatcher
{
    /// <summary>检查访问。</summary>
    bool CheckAccess();

    /// <summary>调用。</summary>
    void Invoke(Action action);

    /// <summary>开始调用。</summary>
    void BeginInvoke(Action action);
}

/// <summary>I导航服务。</summary>
public interface INavigationService
{
    /// <summary>已导航发生变化时触发。</summary>
    event EventHandler<string>? Navigated;

    /// <summary>当前键。</summary>
    string? CurrentKey { get; }

    /// <summary>导航。</summary>
    void Navigate(string key);
}

/// <summary>无界面环境（测试）使用的同步调度器</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    /// <summary>检查访问。</summary>
    public bool CheckAccess() => true;

    /// <summary>调用。</summary>
    public void Invoke(Action action) => action();

    /// <summary>开始调用。</summary>
    public void BeginInvoke(Action action) => action();
}
