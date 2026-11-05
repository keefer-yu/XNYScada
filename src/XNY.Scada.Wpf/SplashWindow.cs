using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XNY.Scada.Wpf;

/// <summary>启动画面：显示运行时启动步骤</summary>
public sealed class SplashWindow : Window
{
    private readonly TextBlock _message;

    public SplashWindow()
    {
        Width = 460;
        Height = 220;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3A));
        Topmost = true;
        ShowInTaskbar = true;
        Title = "XNY SCADA";

        _message = new TextBlock { Foreground = Brushes.LightGray, Margin = new Thickness(0, 16, 0, 0), Text = "正在启动…" };
        Content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "XNY 新能源单机 SCADA", Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold },
                new TextBlock { Text = "组盘 · 拆盘 · 分档 · NG 挑选", Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center },
                new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 20, 0, 0) },
                _message
            }
        };
    }

    public string Message
    {
        set => _message.Text = value;
    }
}
