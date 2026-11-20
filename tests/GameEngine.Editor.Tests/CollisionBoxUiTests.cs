using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameEngine.Core;
using Xunit;

namespace GameEngine.Editor.Tests;

public class CollisionBoxUiTests
{
    static (ObjectEditorWindow window, PixelCanvas canvas) Open(string type)
    {
        var p = SampleProject.Create();
        var w = new ObjectEditorWindow(p, p.FindType(type)!) { Width = 1240, Height = 820 };
        w.Show();
        w.Sprite.SetTool(PixelTool.Collision);
        Dispatcher.UIThread.RunJobs();
        return (w, w.GetVisualDescendants().OfType<PixelCanvas>().Single());
    }

    static Point At(Window w, PixelCanvas c, double x, double y) => c.TranslatePoint(c.ToControl(x, y), w)!.Value;

    static void Drag(Window w, Point from, Point to)
    {
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        w.MouseMove(to, RawInputModifiers.LeftMouseButton);
        w.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Dragging_inside_the_outline_moves_the_box()
    {
        var (w, c) = Open("Player");          // box (1,1,10,15) on a 12x16 sprite
        Drag(w, At(w, c, 5, 8), At(w, c, 6, 8));
        Assert.Equal((2, 1, 10, 15), w.Working.Box);
    }

    [AvaloniaFact]
    public void Dragging_a_corner_handle_resizes_the_box()
    {
        var (w, c) = Open("Player");
        Drag(w, At(w, c, 11, 16), At(w, c, 9, 12));   // bottom-right corner
        Assert.Equal((1, 1, 8, 11), w.Working.Box);
    }

    [AvaloniaFact]
    public void Dragging_outside_the_outline_draws_a_new_box()
    {
        var (w, c) = Open("Gem");             // box (2,2,8,8) on a 12x12 sprite
        Drag(w, At(w, c, 0.2, 0.2), At(w, c, 4, 5));
        Assert.Equal((0, 0, 4, 5), w.Working.Box);
    }

    [AvaloniaFact]
    public void Collision_tool_renders()
    {
        var (w, _) = Open("Slime");
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, "object-collision.png"));
        }
    }
}
