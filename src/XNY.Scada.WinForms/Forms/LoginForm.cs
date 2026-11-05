using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Forms;

/// <summary>登录：账号密码或刷卡（HID 读卡器模拟键盘输入卡号后回车）</summary>
public sealed class LoginForm : Form
{
    private readonly List<IDisposable> _bindings = [];

    public LoginForm(LoginViewModel vm)
    {
        Text = "用户登录";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 330);
        Padding = new Padding(20);

        var user = new TextBox();
        var password = new TextBox { UseSystemPasswordChar = true };
        var card = new TextBox();
        _bindings.Add(Bind.TextBox(user, vm, nameof(LoginViewModel.UserName)));
        _bindings.Add(Bind.TextBox(card, vm, nameof(LoginViewModel.CardNo)));
        vm.Password = "";
        password.TextChanged += (_, _) => vm.Password = password.Text;

        var login = new Button { Text = "登录", Height = 34, Dock = DockStyle.Fill, BackColor = Ui.Primary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _bindings.Add(Bind.Command(login, vm.LoginCommand));
        password.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Bind.Execute(vm.LoginCommand);
        };
        user.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            password.Focus();
        };
        card.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Bind.Execute(vm.CardLoginCommand);
        };

        var error = new Label { ForeColor = Ui.Danger, AutoSize = true, MaximumSize = new Size(330, 0) };
        _bindings.Add(Bind.Text(error, vm, nameof(LoginViewModel.ErrorMessage)));

        var stack = Ui.Stack(
            Ui.Title("账号登录"),
            Ui.Lbl("用户名"), user,
            Ui.Lbl("密码"), password,
            login,
            Ui.Lbl("或刷卡登录（将卡贴近读卡器，或输入卡号回车）", color: Ui.Muted), card,
            error);
        Controls.Add(stack);

        EventHandler loggedIn = (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        vm.LoggedIn += loggedIn;
        FormClosed += (_, _) =>
        {
            vm.LoggedIn -= loggedIn;
            _bindings.ForEach(b => b.Dispose());
        };
        Shown += (_, _) => user.Focus();
    }
}
