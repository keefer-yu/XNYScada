using XNY.Scada.Presentation.Services;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Forms;

/// <summary>闪屏窗体。</summary>
public sealed class SplashForm : Form
{
    /// <summary>消息。</summary>
    private readonly Label _message;

    /// <summary>创建闪屏窗体。</summary>
    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(460, 200);
        BackColor = Ui.HeaderBack;
        ShowInTaskbar = false;
        TopMost = true;
        var title = new Label
        {
            Text = "XNY 新能源 SCADA",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 110,
            TextAlign = ContentAlignment.BottomCenter
        };
        _message = new Label
        {
            Text = "正在启动…",
            ForeColor = Color.FromArgb(0xA0, 0xAE, 0xC0),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_message);
        Controls.Add(title);
    }

    /// <summary>消息。</summary>
    public string Message
    {
        set => _message.Text = value;
    }
}

/// <summary>简单表单对话框基类：内容区 + 确定/取消</summary>
public abstract class FormDialog : Form
{
    /// <summary>循环体。</summary>
    private readonly TableLayoutPanel _body;

    /// <summary>创建窗体对话框。</summary>
    protected FormDialog(string title, int width = 400)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Width = width;
        Padding = new Padding(16);

        _body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Width = width - 32 };
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width - 32));

        var ok = new Button { Text = "确定", Width = 90, Height = 30, BackColor = Ui.Primary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        ok.Click += (_, _) =>
        {
            if (!ValidateInput()) return;
            DialogResult = DialogResult.OK;
        };
        var cancel = new Button { Text = "取消", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
        AcceptButton = ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(buttons);
        Controls.Add(_body);
        Shown += (_, _) => FirstInput?.Focus();
    }

    /// <summary>首条输入。</summary>
    protected Control? FirstInput { get; set; }

    /// <summary>错误提示。</summary>
    protected Label ErrorText { get; } = new() { ForeColor = Ui.Danger, AutoSize = true, MaximumSize = new Size(360, 0) };

    /// <summary>添加标签。</summary>
    protected void AddLabel(string text) =>
        Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(0, 8, 0, 2) });

    /// <summary>添加。</summary>
    protected T Add<T>(T control) where T : Control
    {
        if (control is TextBox) control.Dock = DockStyle.Fill;
        _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _body.Controls.Add(control, 0, _body.RowCount++);
        if (FirstInput == null && control is TextBox) FirstInput = control;
        return control;
    }

    /// <summary>校验输入。</summary>
    protected virtual bool ValidateInput() => true;
}

/// <summary>提示窗体。</summary>
public sealed class PromptForm : FormDialog
{
    /// <summary>输入。</summary>
    private readonly TextBox _input;

    /// <summary>创建提示窗体。</summary>
    public PromptForm(string title, string message, string defaultValue) : base(title)
    {
        AddLabel(message);
        _input = Add(new TextBox { Text = defaultValue });
        Shown += (_, _) => _input.SelectAll();
    }

    /// <summary>提示窗体值。</summary>
    public string Value => _input.Text;
}

/// <summary>签名窗体。</summary>
public sealed class SignatureForm : FormDialog
{
    /// <summary>用户。</summary>
    private readonly TextBox _user;
    /// <summary>密码。</summary>
    private readonly TextBox _password;
    /// <summary>注释。</summary>
    private readonly TextBox _comment;

    /// <summary>创建签名窗体。</summary>
    public SignatureForm(string reason) : base("电子签名")
    {
        AddLabel("操作：" + reason);
        AddLabel("签名人账号");
        _user = Add(new TextBox());
        AddLabel("密码");
        _password = Add(new TextBox { UseSystemPasswordChar = true });
        AddLabel("备注（可选）");
        _comment = Add(new TextBox());
        Add(ErrorText);
    }

    /// <summary>签名窗体结果。</summary>
    public SignatureCredential? Result { get; private set; }

    /// <summary>校验输入。</summary>
    protected override bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(_user.Text) || _password.Text.Length == 0)
        {
            ErrorText.Text = "请输入签名人账号和密码";
            return false;
        }
        Result = new SignatureCredential(_user.Text.Trim(), _password.Text, string.IsNullOrWhiteSpace(_comment.Text) ? null : _comment.Text);
        return true;
    }
}

/// <summary>更改密码窗体。</summary>
public sealed class ChangePasswordForm : FormDialog
{
    /// <summary>旧。</summary>
    private readonly TextBox _old;
    /// <summary>新。</summary>
    private readonly TextBox _new;
    /// <summary>确认。</summary>
    private readonly TextBox _confirm;

    /// <summary>创建更改密码窗体。</summary>
    public ChangePasswordForm() : base("修改密码")
    {
        AddLabel("原密码");
        _old = Add(new TextBox { UseSystemPasswordChar = true });
        AddLabel("新密码");
        _new = Add(new TextBox { UseSystemPasswordChar = true });
        AddLabel("确认新密码");
        _confirm = Add(new TextBox { UseSystemPasswordChar = true });
        Add(ErrorText);
    }

    /// <summary>更改密码窗体结果。</summary>
    public PasswordChange? Result { get; private set; }

    /// <summary>校验输入。</summary>
    protected override bool ValidateInput()
    {
        if (_new.Text.Length == 0 || _new.Text != _confirm.Text)
        {
            ErrorText.Text = "两次输入的新密码不一致";
            return false;
        }
        Result = new PasswordChange(_old.Text, _new.Text);
        return true;
    }
}
