using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TerminalHub.App.Views;

namespace TerminalHub.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Apply the persisted theme now — the VM re-applies it later, but doing
        // it here first prevents a DarkGlass flash for users on another theme.
        Controls.ThemeManager.Apply(new TerminalHub.Core.Settings.SettingsStore().Load().Theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            Program.Activation?.Attach(Activate, (path, error) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Activate();
                    if (desktop.MainWindow is MainWindow window)
                        window.AcceptLaunch(path, error);
                }));

            void Activate() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var window = desktop.MainWindow;
                if (window is null) return;
                if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
                    window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Show();
                window.Activate();
            });
        }
        base.OnFrameworkInitializationCompleted();
    }
}
