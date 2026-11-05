using System.Windows;
using System.Windows.Controls;
using XNY.Scada.Presentation.Services;

namespace XNY.Scada.Wpf.Views.Dialogs;

/// <summary>简单表单对话框基类：标题、内容区、确定/取消</summary>
public abstract class FormDialog : Window
{
    /// <summary>循环体。</summary>
    private readonly StackPanel _body = new() { Margin = new Thickness(16) };

    /// <summary>创建窗体对话框。</summary>
    protected FormDialog(string title, double width = 380)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var ok = new Button { Content = "确定", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "ButtonPrimary");
        ok.Click += (_, _) =>
        {
            if (!Validate()) return;
            DialogResult = true;
        };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var outer = new StackPanel();
        outer.Children.Add(_body);
        var footer = new Border { Padding = new Thickness(16, 0, 16, 16), Child = buttons };
        outer.Children.Add(footer);
        Content = outer;
        Loaded += (_, _) => FirstInput?.Focus();
    }

    /// <summary>首条输入。</summary>
    protected Control? FirstInput { get; set; }

    /// <summary>错误提示。</summary>
    protected TextBlock ErrorText { get; } = new() { Foreground = StatusBrushConverter.Bad, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };

    /// <summary>添加标签。</summary>
    protected void AddLabel(string text) => _body.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap });

    /// <summary>添加。</summary>
    protected T Add<T>(T element) where T : UIElement
    {
        _body.Children.Add(element);
        if (FirstInput == null && element is Control c and (TextBox or PasswordBox)) FirstInput = c;
        return element;
    }

    /// <summary>校验。</summary>
    protected virtual bool Validate() => true;
}

/// <summary>提示对话框。</summary>
public sealed class PromptDialog : FormDialog
{
    /// <summary>输入。</summary>
    private readonly TextBox _input;

    /// <summary>创建提示对话框。</summary>
    public PromptDialog(string title, string message, string defaultValue) : base(title)
    {
        AddLabel(message);
        _input = Add(new TextBox { Text = defaultValue });
        _input.SelectAll();
    }

    /// <summary>提示对话框值。</summary>
    public string Value => _input.Text;
}

/// <summary>电子签名：签名人账号 + 密码 + 备注，校验在调用方完成</summary>
public sealed class SignatureDialog : FormDialog
{
    /// <summary>用户。</summary>
    private readonly TextBox _user;
    /// <summary>密码。</summary>
    private readonly PasswordBox _password;
    /// <summary>注释。</summary>
    private readonly TextBox _comment;

    /// <summary>创建签名对话框。</summary>
    public SignatureDialog(string reason) : base("电子签名")
    {
        AddLabel("操作：" + reason);
        AddLabel("签名人账号");
        _user = Add(new TextBox());
        AddLabel("密码");
        _password = Add(new PasswordBox());
        AddLabel("备注（可选）");
        _comment = Add(new TextBox());
        Add(ErrorText);
    }

    /// <summary>签名对话框结果。</summary>
    public SignatureCredential? Result { get; private set; }

    /// <summary>校验。</summary>
    protected override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(_user.Text) || _password.Password.Length == 0)
        {
            ErrorText.Text = "请输入签名人账号和密码";
            return false;
        }
        Result = new SignatureCredential(_user.Text.Trim(), _password.Password, string.IsNullOrWhiteSpace(_comment.Text) ? null : _comment.Text);
        return true;
    }
}

/// <summary>更改密码对话框。</summary>
public sealed class ChangePasswordDialog : FormDialog
{
    /// <summary>旧。</summary>
    private readonly PasswordBox _old;
    /// <summary>新。</summary>
    private readonly PasswordBox _new;
    /// <summary>确认。</summary>
    private readonly PasswordBox _confirm;

    /// <summary>创建更改密码对话框。</summary>
    public ChangePasswordDialog() : base("修改密码")
    {
        AddLabel("原密码");
        _old = Add(new PasswordBox());
        AddLabel("新密码");
        _new = Add(new PasswordBox());
        AddLabel("确认新密码");
        _confirm = Add(new PasswordBox());
        Add(ErrorText);
    }

    /// <summary>更改密码对话框结果。</summary>
    public PasswordChange? Result { get; private set; }

    /// <summary>校验。</summary>
    protected override bool Validate()
    {
        if (_new.Password.Length == 0 || _new.Password != _confirm.Password)
        {
            ErrorText.Text = "两次输入的新密码不一致";
            return false;
        }
        Result = new PasswordChange(_old.Password, _new.Password);
        return true;
    }
}
