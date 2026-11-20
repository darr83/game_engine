using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GameEngine.Core;

namespace GameEngine.Desktop;

/// <summary>A control that runs a <see cref="World"/> and draws it every frame.</summary>
public sealed class GameView : Control
{
    readonly Stopwatch _clock = new();
    TimeSpan _last;
    bool _running;

    public GameView(World world)
    {
        World = world;
        Focusable = true;
        ClipToBounds = true;
    }

    public World World { get; private set; }

    public void Reset(World world)
    {
        World = world;
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _running = true;
        _clock.Restart();
        _last = TimeSpan.Zero;
        RequestFrame();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _running = false;
        base.OnDetachedFromVisualTree(e);
    }

    void RequestFrame() => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);

    void OnFrame(TimeSpan _)
    {
        if (!_running) return;
        var now = _clock.Elapsed;
        World.Update((now - _last).TotalSeconds);
        _last = now;
        InvalidateVisual();
        RequestFrame();
    }

    public override void Render(DrawingContext context) => SceneRenderer.DrawWorld(context, World, new Rect(Bounds.Size));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (KeyName(e.Key) is { } k) { World.Input.KeyDown(k); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (KeyName(e.Key) is { } k) World.Input.KeyUp(k);
        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        World.Input.Clear();
        base.OnLostFocus(e);
    }

    /// <summary>Maps Avalonia keys to the engine's key names.</summary>
    public static string? KeyName(Key key) => key switch
    {
        Key.Space => "Space",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Enter => "Enter",
        Key.Escape => "Escape",
        Key.LeftShift or Key.RightShift => "Shift",
        >= Key.A and <= Key.Z => key.ToString(),
        _ => null,
    };
}

/// <summary>A window that plays a game. Used by "Play" in the editor and by exported games.</summary>
public sealed class GameWindow : Window
{
    readonly Project _project;
    readonly int _startLevel;
    readonly bool _fromEditor;

    public GameWindow(Project project, int startLevel = 0, bool fromEditor = false)
    {
        _project = project;
        _startLevel = startLevel;
        _fromEditor = fromEditor;
        Title = fromEditor ? $"{project.Name} — Playing (Esc to stop, R to restart)" : project.Name;
        Width = 1280;
        Height = 720;
        MinWidth = 480;
        MinHeight = 300;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        View = new GameView(new World(project.Clone(), startLevel));
        Content = View;
        Activated += (_, _) => View.Focus();
        // Tunnel so the window sees Esc/R/F11 before the game view handles them.
        AddHandler(KeyDownEvent, OnWindowKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public GameView View { get; }

    void OnWindowKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (_fromEditor && e.Key == Key.R && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            View.Reset(new World(_project.Clone(), _startLevel));
        }
        else if (e.Key == Key.F11 || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            e.Handled = true;
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }
    }
}
