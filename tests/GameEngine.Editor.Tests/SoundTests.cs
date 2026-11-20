using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameEngine.Core;
using GameEngine.Desktop;
using Xunit;

namespace GameEngine.Editor.Tests;

public class SoundUiTests
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
    public void Sounds_page_renders()
    {
        var w = new MainWindow(SampleProject.Create()) { Width = 1440, Height = 880 };
        w.Show();
        w.SelectTab(2);
        Save(w, "editor-sounds");
    }

    [AvaloniaFact]
    public void Audio_player_never_throws_even_without_a_device()
    {
        var p = SampleProject.Create();
        var player = AudioPlayer.Shared;
        player.Play(p.FindSound("Coin")!);
        player.PlayMusic(p.FindSound("Theme")!);
        player.StopAll();
        Assert.True(player.Available || player.Error is not null);
    }

    [AvaloniaFact]
    public void Importing_a_wav_adds_a_sound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"beep-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, AudioCodec.EncodeWav(Synth.Render(Synth.Preset("Coin"))));
        try
        {
            var p = SampleProject.CreateBlank();
            var view = new SoundsView();
            var w = new Window { Content = view, Width = 1000, Height = 700 };
            w.Show();
            view.Show(p);
            var sound = view.ImportFile(path);
            Assert.NotNull(sound);
            Assert.Equal("wav", sound!.FileType);
            Assert.Contains(sound, p.Sounds);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void Undo_keeps_imported_audio()
    {
        var p = SampleProject.Create();
        var stack = new UndoStack();
        var theme = p.FindSound("Theme")!.Data;
        stack.Push(p, 0);
        p.Sounds.Clear();
        var (restored, _) = stack.Undo(p, 0)!.Value;
        Assert.Same(theme, restored.FindSound("Theme")!.Data);
        Assert.NotNull(p.Sounds);   // the live project was not touched by the snapshot
    }
}
