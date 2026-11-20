using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Player;

/// <summary>
/// The stand-alone game runner. "Export" in the editor copies this program next to a
/// game.json file and renames it after the game.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<PlayerApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}

public sealed class PlayerApp : Application
{
    public override void Initialize() => UiTheme.Apply(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var path = desktop.Args is { Length: > 0 } a ? a[0] : Path.Combine(AppContext.BaseDirectory, "game.json");
            Project project;
            try
            {
                project = ProjectIO.Load(path);
            }
            catch (Exception ex)
            {
                desktop.MainWindow = new Window
                {
                    Title = "Game_Engine",
                    Width = 520, Height = 200,
                    Content = new TextBlock
                    {
                        Text = $"Could not load the game from {path}.\n\n{ex.Message}",
                        Margin = new Thickness(24),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                };
                base.OnFrameworkInitializationCompleted();
                return;
            }
            desktop.MainWindow = new GameWindow(project) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
