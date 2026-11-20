using Xunit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using GameEngine.Core;
using GameEngine.Desktop;
using GameEngine.Editor;

[assembly: AvaloniaTestApplication(typeof(GameEngine.Editor.Tests.TestApp))]

namespace GameEngine.Editor.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Opens the real windows off-screen and renders them. Set SCREENSHOT_DIR to save the frames as PNGs.
/// </summary>
public class EditorTests
{
    static void Save(Window w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name + ".png"));
        }
    }

    [AvaloniaFact]
    public void Editor_opens_with_the_sample_level()
    {
        var w = new MainWindow(SampleProject.Create()) { Width = 1440, Height = 880 };
        w.Show();
        Save(w, "editor-level");
    }

    [AvaloniaFact]
    public void Event_sheet_renders()
    {
        var w = new MainWindow(SampleProject.Create()) { Width = 1440, Height = 880 };
        w.Show();
        w.SelectTab(1);
        Save(w, "editor-events");
    }

    [AvaloniaFact]
    public void Condition_dialog_renders()
    {
        var p = SampleProject.Create();
        var d = new EventItemDialog(p, true, p.Events[0].Conditions[0]);
        d.Show();
        Save(d, "dialog-condition");
    }

    [AvaloniaFact]
    public void Game_window_plays()
    {
        var w = new GameWindow(SampleProject.Create()) { Width = 1280, Height = 720 };
        w.Show();
        var world = w.View.World;
        world.Input.KeyDown("Right");
        for (var i = 0; i < 70; i++) world.Step();
        world.Input.KeyDown("Space");
        for (var i = 0; i < 12; i++) world.Step();
        w.View.InvalidateVisual();
        Save(w, "game");
        Assert.True(world.Entities.First(e => e.Type == "Player").X > 60);
    }

    [AvaloniaFact]
    public void Dropping_an_object_places_and_selects_it()
    {
        var p = SampleProject.CreateBlank();
        var canvas = new LevelCanvas(p, p.Levels[0]);
        var w = new Window { Width = 800, Height = 500, Content = canvas };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var before = p.Levels[0].Instances.Count;
        var inst = canvas.PlaceAt("MovingPlatform", new Point(300, 150));
        Assert.NotNull(inst);
        Assert.Equal(before + 1, p.Levels[0].Instances.Count);
        Assert.Same(inst, canvas.Selected);
        Assert.Equal(0, inst!.X % 16);
    }

    [AvaloniaFact]
    public void Export_writes_a_runnable_game_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ge-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = SampleProject.Create();
            p.Name = "Coin Quest";
            var result = Exporter.Export(p, dir);
            Assert.True(result.Ok, result.Message);
            var game = Path.Combine(dir, "Coin Quest");
            Assert.True(File.Exists(Path.Combine(game, "game.json")));
            Assert.True(File.Exists(Path.Combine(game, "GameEngine.Player.dll")));
            Assert.True(File.Exists(Path.Combine(game, "Coin Quest")) || File.Exists(Path.Combine(game, "Coin Quest.exe")));
            Assert.Equal(p.Levels.Count, ProjectIO.Load(Path.Combine(game, "game.json")).Levels.Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
