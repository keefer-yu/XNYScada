using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XNY.Scada.Presentation.ViewModels;

namespace XNY.Scada.Wpf;

/// <summary>主窗口。</summary>
public partial class MainWindow
{
    /// <summary>视图模型。</summary>
    private readonly ShellViewModel _vm;

    /// <summary>创建主窗口。</summary>
    public MainWindow(ShellViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        PreviewMouseDown += (_, _) => vm.NotifyActivity();
        PreviewKeyDown += (_, _) => vm.NotifyActivity();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellViewModel.IsLoginVisible) || !vm.IsLoginVisible) return;
            PasswordBox.Clear();
            Dispatcher.BeginInvoke(() => UserNameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    /// <summary>当密码变化时更新关联状态。</summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _vm.Login.Password = ((PasswordBox)sender).Password;

    /// <summary>处理密码键下。</summary>
    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.Login.LoginCommand.CanExecute(null)) _vm.Login.LoginCommand.Execute(null);
    }

    /// <summary>处理关闭中。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (MessageBox.Show(this, "退出后设备通讯与流程将全部停止，确定退出？", "退出", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _vm.CurrentPage?.Deactivate();
        base.OnClosing(e);
    }
}
