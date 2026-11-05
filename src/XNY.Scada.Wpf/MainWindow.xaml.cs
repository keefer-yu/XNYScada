using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XNY.Scada.Presentation.ViewModels;

namespace XNY.Scada.Wpf;

public partial class MainWindow
{
    private readonly ShellViewModel _vm;

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

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _vm.Login.Password = ((PasswordBox)sender).Password;

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.Login.LoginCommand.CanExecute(null)) _vm.Login.LoginCommand.Execute(null);
    }

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
