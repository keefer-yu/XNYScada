using System.ComponentModel;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;
using XNY.Scada.WinForms.Pages;

namespace XNY.Scada.WinForms.Forms;

/// <summary>主窗体。</summary>
public sealed class MainForm : Form, IMessageFilter
{
    /// <summary>视图模型。</summary>
    private readonly ShellViewModel _vm;
    /// <summary>内容。</summary>
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.PageBack, Padding = new Padding(4) };
    /// <summary>页面。</summary>
    private readonly Dictionary<ViewModelBase, Control> _pages = new();
    /// <summary>确认条。</summary>
    private readonly FlowLayoutPanel _confirmBar = new()
    {
        Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        BackColor = Color.FromArgb(0xFF, 0xF7, 0xE6), Padding = new Padding(8, 4, 8, 4)
    };
    /// <summary>主窗体状态。</summary>
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    /// <summary>页面状态。</summary>
    private readonly ToolStripStatusLabel _pageStatus = new() { TextAlign = ContentAlignment.MiddleRight };
    /// <summary>忙碌。</summary>
    private readonly ToolStripProgressBar _busy = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 80 };
    /// <summary>页面绑定。</summary>
    private readonly List<IDisposable> _pageBindings = [];
    /// <summary>登录正在显示。</summary>
    private bool _loginShowing;

    /// <summary>创建主窗体。</summary>
    public MainForm(ShellViewModel vm)
    {
        _vm = vm;
        Text = vm.Title;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1280, 760);
        BackColor = Ui.PageBack;
        KeyPreview = true;

        var header = BuildHeader();
        var menu = BuildMenu();
        var statusStrip = new StatusStrip();
        statusStrip.Items.AddRange([_status, _busy, _pageStatus]);

        Controls.Add(_content);
        Controls.Add(_confirmBar);
        Controls.Add(menu);
        Controls.Add(header);
        Controls.Add(statusStrip);

        Bind.Text(_status, vm, nameof(ShellViewModel.StatusMessage));
        Bind.Observe(vm, nameof(ShellViewModel.CurrentPage), ShowPage);
        Bind.Observe(vm.PendingConfirmations, RebuildConfirmations);
        Bind.Observe(vm, nameof(ShellViewModel.IsLoginVisible), () =>
        {
            if (vm.IsLoginVisible && !_loginShowing) BeginInvoke(ShowLogin);
        }, invokeNow: false);

        System.Windows.Forms.Application.AddMessageFilter(this);
        Shown += (_, _) =>
        {
            if (!vm.IsLoggedIn) ShowLogin();
        };
    }

    /// <summary>构建标题。</summary>
    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Ui.HeaderBack, Padding = new Padding(12, 0, 12, 0) };
        var title = new Label { AutoSize = true, ForeColor = Color.White, Font = new Font(Font.FontFamily, 13f, FontStyle.Bold), Margin = new Padding(0, 14, 16, 0) };
        Bind.Text(title, _vm, nameof(ShellViewModel.MachineTitle));
        var recipe = new Label { AutoSize = true, ForeColor = Color.FromArgb(0x9A, 0xE6, 0xB4), Margin = new Padding(0, 17, 16, 0) };
        Bind.Text(recipe, _vm, nameof(ShellViewModel.RecipeName), v => "配方：" + v);
        var alarm = new Label
        {
            AutoSize = true, ForeColor = Color.White, BackColor = Ui.Danger, Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 12, 0, 0), Cursor = Cursors.Hand
        };
        Bind.Observe(_vm, nameof(ShellViewModel.LatestAlarm), () =>
        {
            alarm.Visible = _vm.ActiveAlarmCount > 0;
            alarm.Text = $"⚠ {_vm.ActiveAlarmCount} 条报警  {_vm.LatestAlarm}";
        });
        Bind.Observe(_vm, nameof(ShellViewModel.ActiveAlarmCount), () => alarm.Visible = _vm.ActiveAlarmCount > 0);
        alarm.Click += (_, _) => _vm.Navigate("Alarm");

        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
        left.Controls.AddRange([title, recipe, alarm]);

        var user = new Label { AutoSize = true, ForeColor = Color.White, Margin = new Padding(0, 17, 10, 0) };
        Bind.Text(user, _vm, nameof(ShellViewModel.UserDisplay));
        var login = HeaderButton("登录", _vm.ShowLoginCommand);
        var changePassword = HeaderButton("改密", _vm.ChangePasswordCommand);
        var logout = HeaderButton("登出", _vm.LogoutCommand);
        Bind.Visible(login, _vm, nameof(ShellViewModel.IsLoggedIn), invert: true);
        Bind.Visible(changePassword, _vm, nameof(ShellViewModel.IsLoggedIn));
        Bind.Visible(logout, _vm, nameof(ShellViewModel.IsLoggedIn));
        var clock = new Label { AutoSize = true, ForeColor = Color.FromArgb(0xA0, 0xAE, 0xC0), Margin = new Padding(10, 17, 0, 0) };
        Bind.Text(clock, _vm, nameof(ShellViewModel.Now), v => ((DateTime)v!).ToString("yyyy-MM-dd HH:mm:ss"));

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
        right.Controls.AddRange([user, login, changePassword, logout, clock]);

        header.Controls.Add(left);
        header.Controls.Add(right);
        return header;
    }

    /// <summary>标题键。</summary>
    private static Button HeaderButton(string text, System.Windows.Input.ICommand command)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Ui.MenuBack,
            Margin = new Padding(0, 12, 6, 0), Height = 28, Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(0x4A, 0x55, 0x68);
        Bind.Command(b, command);
        return b;
    }

    /// <summary>构建菜单。</summary>
    private Control BuildMenu()
    {
        var menu = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, Width = 150, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Ui.MenuBack, Padding = new Padding(0, 8, 0, 0), AutoScroll = true
        };
        foreach (var item in _vm.MenuItems)
        {
            var b = new Button
            {
                Text = "  " + item.Title, Width = 150, Height = 44, FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Cursor = Cursors.Hand,
                Font = new Font(Font.FontFamily, 10.5f)
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => _vm.Navigate(item.Key);
            Bind.Visible(b, item, nameof(MenuItemViewModel.IsVisible));
            Bind.Observe(item, nameof(MenuItemViewModel.IsSelected), () => b.BackColor = item.IsSelected ? Ui.Primary : Ui.MenuBack);
            menu.Controls.Add(b);
        }
        return menu;
    }

    /// <summary>显示页面。</summary>
    private void ShowPage()
    {
        _pageBindings.ForEach(b => b.Dispose());
        _pageBindings.Clear();
        var page = _vm.CurrentPage;
        if (page == null) return;
        if (!_pages.TryGetValue(page, out var control))
        {
            control = PageFactory.Create(page);
            control.Dock = DockStyle.Fill;
            _pages[page] = control;
        }
        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.Controls.Add(control);
        _content.ResumeLayout();
        _pageBindings.Add(Bind.Text(_pageStatus, page, nameof(ViewModelBase.StatusMessage)));
        _pageBindings.Add(Bind.Observe(page, nameof(ViewModelBase.IsBusy), () => _busy.Visible = page.IsBusy));
    }

    /// <summary>重新生成确认。</summary>
    private void RebuildConfirmations()
    {
        _confirmBar.SuspendLayout();
        foreach (Control c in _confirmBar.Controls) c.Dispose();
        _confirmBar.Controls.Clear();
        foreach (var r in _vm.PendingConfirmations)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            row.Controls.Add(Ui.Lbl($"【{r.Title}】{r.Message}（{r.Deadline:HH:mm:ss} 前确认）", bold: true));
            row.Controls.Add(Ui.Btn("是", r.YesCommand, ButtonKind.Primary));
            row.Controls.Add(Ui.Btn("否", r.NoCommand));
            _confirmBar.Controls.Add(row);
        }
        _confirmBar.Visible = _vm.PendingConfirmations.Count > 0;
        _confirmBar.ResumeLayout();
    }

    /// <summary>显示登录。</summary>
    private void ShowLogin()
    {
        if (_loginShowing) return;
        _loginShowing = true;
        try
        {
            using var form = new LoginForm(_vm.Login);
            form.ShowDialog(this);
        }
        finally
        {
            _loginShowing = false;
            Bind.Execute(_vm.CloseLoginCommand);
        }
    }

    /// <summary>前置筛选消息。</summary>
    public bool PreFilterMessage(ref Message m)
    {
        // WM_KEYDOWN / WM_LBUTTONDOWN / WM_RBUTTONDOWN / WM_MOUSEWHEEL 视为操作活动，用于自动登出计时
        if (m.Msg is 0x0100 or 0x0201 or 0x0204 or 0x020A) _vm.NotifyActivity();
        return false;
    }

    /// <summary>处理窗体关闭中。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, "退出后设备通讯与流程将全部停止，确定退出？", "退出", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        System.Windows.Forms.Application.RemoveMessageFilter(this);
        _vm.CurrentPage?.Deactivate();
        base.OnFormClosing(e);
    }
}
