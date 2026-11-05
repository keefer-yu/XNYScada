using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

/// <summary>登录页面的视图模型。</summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    /// <summary>卡阅读器。</summary>
    private readonly ICardReader _cardReader;

    /// <summary>创建登录视图模型。</summary>
    public LoginViewModel(ViewModelContext context, ICardReader cardReader) : base(context)
    {
        _cardReader = cardReader;
        _cardReader.CardRead += (_, card) => Ui.BeginInvoke(async () => await LoginByCardAsync(card));
        _cardReader.Open();
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);
        CardLoginCommand = new AsyncRelayCommand(() => LoginByCardAsync(CardNo));
    }

    /// <summary>标题。</summary>
    public override string Title => "登录";

    /// <summary>已记录在发生变化时触发。</summary>
    public event EventHandler? LoggedIn;

    /// <summary>用户名称。</summary>
    [ObservableProperty]
    private string _userName = "";

    /// <summary>WPF PasswordBox 不支持绑定，由视图代码设置</summary>
    [ObservableProperty]
    private string _password = "";

    /// <summary>HID 读卡器（键盘模拟）输入的卡号</summary>
    [ObservableProperty]
    private string _cardNo = "";

    /// <summary>错误消息。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>登录命令。</summary>
    public IAsyncRelayCommand LoginCommand { get; }

    /// <summary>卡登录命令。</summary>
    public IAsyncRelayCommand CardLoginCommand { get; }

    /// <summary>登录。</summary>
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(UserName))
        {
            ErrorMessage = "请输入用户名";
            return;
        }
        IsBusy = true;
        try
        {
            var r = await Context.Auth.LoginAsync(UserName.Trim(), Password);
            await AfterLoginAsync(r);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>登录按卡。</summary>
    private async Task LoginByCardAsync(string card)
    {
        if (string.IsNullOrWhiteSpace(card)) return;
        ErrorMessage = null;
        var r = await Context.Auth.LoginByCardAsync(card);
        CardNo = "";
        await AfterLoginAsync(r);
    }

    /// <summary>之后登录。</summary>
    private async Task AfterLoginAsync(LoginResult r)
    {
        if (!r.Success)
        {
            ErrorMessage = r.Message;
            return;
        }
        Password = "";
        if (r.MustChangePassword)
        {
            Dialogs.ShowInfo(r.Message);
            var change = Dialogs.RequestPasswordChange();
            if (change != null)
            {
                var err = await Context.Auth.ChangePasswordAsync(change.OldPassword, change.NewPassword);
                if (err != null) Dialogs.ShowError(err);
            }
        }
        LoggedIn?.Invoke(this, EventArgs.Empty);
    }
}
