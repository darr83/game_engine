using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using GameEngine.Core;
using GameEngine.Desktop;
using Xunit;

namespace GameEngine.Editor.Tests;

public class ObjectEditorTests
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
    public void Object_editor_shows_the_sprite_page()
    {
        var p = SampleProject.Create();
        var w = new ObjectEditorWindow(p, p.FindType("Player")!) { Width = 1240, Height = 820 };
        w.Show();
        Save(w, "object-sprite");
    }

    [AvaloniaFact]
    public void Object_editor_shows_the_behaviour_page()
    {
        var p = SampleProject.Create();
        var w = new ObjectEditorWindow(p, p.FindType("Player")!) { Width = 1240, Height = 820 };
        w.Show();
        w.ShowPage(1);
        Save(w, "object-behaviour");
    }

    [AvaloniaFact]
    public void Drawing_on_the_canvas_changes_the_working_copy_only()
    {
        var p = SampleProject.Create();
        var original = p.FindType("Coin")!;
        var before = original.Icon.Pixels.ToArray();
        var w = new ObjectEditorWindow(p, original) { Width = 1240, Height = 820 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        // click in the middle of the canvas with the pencil
        w.MouseDown(new Point(620, 400), MouseButton.Left);
        w.MouseUp(new Point(620, 400), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(before, w.Working.Icon.Pixels);
        Assert.Equal(before, original.Icon.Pixels);
    }

    [AvaloniaFact]
    public void Sprite_sheets_are_imported_and_sliced_into_frames()
    {
        // a 3-frame 8x8 sheet: red, green, blue squares
        var path = Path.Combine(Path.GetTempPath(), $"sheet-{Guid.NewGuid():N}.png");
        var bmp = new WriteableBitmap(new PixelSize(24, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            var colors = new[] { 0xFFFF0000u, 0xFF00FF00u, 0xFF0000FFu };
            var row = new int[24];
            for (var x = 0; x < 24; x++) row[x] = unchecked((int)colors[x / 8]);
            for (var y = 0; y < 8; y++) System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, 24);
        }
        bmp.Save(path);

        try
        {
            var img = SpriteEditor.LoadPng(path);
            Assert.Equal(24, img.Width);
            Assert.Equal(0xFF00FF00u, img[12, 4]);

            var type = new ObjectType { Id = "Test", Name = "Test", Animations = { new SpriteAnimation { Frames = { new PixelImage(16, 16) } } } };
            var editor = new SpriteEditor(type);
            var count = editor.ImportImage(img, 8, 8);
            Assert.Equal(3, count);
            Assert.Equal(8, type.Width);
            Assert.Equal(0xFF0000FFu, type.Animations[0].Frames[2][0, 0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void Custom_objects_appear_in_the_library()
    {
        var p = SampleProject.Create();
        var w = new MainWindow(p) { Width = 1440, Height = 880 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var library = w.GetVisualDescendants().OfType<ObjectLibrary>().Single();
        Assert.Contains(library.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Gem");
    }
}
