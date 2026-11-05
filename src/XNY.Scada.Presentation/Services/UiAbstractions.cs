namespace XNY.Scada.Presentation.Services;

public sealed record SignatureCredential(string UserName, string Password, string? Comment);

public sealed record PasswordChange(string OldPassword, string NewPassword);

/// <summary>对话框抽象，WPF 与 WinForms 各自实现</summary>
public interface IDialogService
{
    void ShowInfo(string message, string title = "提示");

    void ShowError(string message, string title = "错误");

    bool Confirm(string message, string title = "确认");

    string? Prompt(string message, string title, string defaultValue = "");

    /// <summary>电子签名对话框：输入签名人账号、密码、备注</summary>
    SignatureCredential? RequestSignature(string reason);

    PasswordChange? RequestPasswordChange();

    string? OpenFile(string filter);

    string? SaveFile(string filter, string defaultName);
}

/// <summary>UI 线程调度</summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    void Invoke(Action action);

    void BeginInvoke(Action action);
}

public interface INavigationService
{
    event EventHandler<string>? Navigated;

    string? CurrentKey { get; }

    void Navigate(string key);
}

/// <summary>无界面环境（测试）使用的同步调度器</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Invoke(Action action) => action();

    public void BeginInvoke(Action action) => action();
}
