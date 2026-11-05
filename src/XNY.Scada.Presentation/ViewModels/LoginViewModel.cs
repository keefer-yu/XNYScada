using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Security;

namespace XNY.Scada.Presentation.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly ICardReader _cardReader;

    public LoginViewModel(ViewModelContext context, ICardReader cardReader) : base(context)
    {
        _cardReader = cardReader;
        _cardReader.CardRead += (_, card) => Ui.BeginInvoke(async () => await LoginByCardAsync(card));
        _cardReader.Open();
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);
        CardLoginCommand = new AsyncRelayCommand(() => LoginByCardAsync(CardNo));
    }

    public override string Title => "登录";

    public event EventHandler? LoggedIn;

    [ObservableProperty]
    private string _userName = "";

    /// <summary>WPF PasswordBox 不支持绑定，由视图代码设置</summary>
    [ObservableProperty]
    private string _password = "";

    /// <summary>HID 读卡器（键盘模拟）输入的卡号</summary>
    [ObservableProperty]
    private string _cardNo = "";

    [ObservableProperty]
    private string? _errorMessage;

    public IAsyncRelayCommand LoginCommand { get; }

    public IAsyncRelayCommand CardLoginCommand { get; }

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

    private async Task LoginByCardAsync(string card)
    {
        if (string.IsNullOrWhiteSpace(card)) return;
        ErrorMessage = null;
        var r = await Context.Auth.LoginByCardAsync(card);
        CardNo = "";
        await AfterLoginAsync(r);
    }

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
