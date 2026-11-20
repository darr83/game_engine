using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

public static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize() => UiTheme.Apply(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // open a project passed on the command line (e.g. double-clicking a .geproj)
            var project = desktop.Args is { Length: > 0 } a && File.Exists(a[0]) ? ProjectIO.Load(a[0]) : SampleProject.Create();
            desktop.MainWindow = new MainWindow(project);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
