using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

return AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);

public sealed class ProbeApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "FocusGuard · ensayo Linux (sin bloqueo)",
                Width = 480,
                Height = 230,
                Background = new SolidColorBrush(Color.Parse("#131B1A")),
                Content = new StackPanel
                {
                    Margin = new Thickness(24),
                    Spacing = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Ensayo de shell Linux", FontSize = 25, Foreground = Brushes.White },
                        new TextBlock { Text = "No bloquea aplicaciones ni conecta al cloud.\nLa ventana se cierra automáticamente en 5 segundos.", FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#B9D0C6")) }
                    }
                }
            };
            desktop.MainWindow.Opened += (_, _) =>
            {
                Console.WriteLine("SHELL_WINDOW_OPENED");
                DispatcherTimer.RunOnce(() => { Console.WriteLine("SHELL_EXIT_NORMAL"); desktop.Shutdown(0); }, TimeSpan.FromSeconds(5));
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
