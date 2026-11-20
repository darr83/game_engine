using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

public enum PixelTool { Pencil, Eraser, Fill, Picker, Collision }

/// <summary>A zoomed-in canvas for drawing one frame pixel by pixel.</summary>
public sealed class PixelCanvas : Control
{
    static readonly RenderOptions Pixelated = new() { BitmapInterpolationMode = BitmapInterpolationMode.None, EdgeMode = EdgeMode.Aliased };

    PixelImage _image = new(16, 16);
    (int x, int y)? _hover, _last;
    bool _drawing, _erasing;

    public PixelCanvas()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public PixelImage Image
    {
        get => _image;
        set { _image = value; InvalidateVisual(); }
    }

    /// <summary>The previous frame, drawn faintly underneath to help animate.</summary>
    public PixelImage? Onion { get; set; }
    public PixelTool Tool { get; set; } = PixelTool.Pencil;
    public uint Color { get; set; } = 0xFF1B1D2A;
    public bool Mirror { get; set; }

    public event Action? Changing;
    public event Action? Changed;
    public event Action<uint>? ColorPicked;

    /// <summary>The object whose collision box is shown and edited with the Collision tool.</summary>
    public ObjectType? BoxOwner { get; set; }
    public event Action? BoxChanging;
    public event Action? BoxChanged;

    // collision box dragging
    enum BoxDrag { None, Move, Resize, Draw }
    BoxDrag _boxDrag;
    bool _dragLeft, _dragRight, _dragTop, _dragBottom, _boxSnapshotTaken;
    (int X, int Y, int W, int H) _boxStart;
    (int X, int Y) _gridStart;
    const double HandleSize = 8;

    (double scale, Point origin) Layout()
    {
        var fit = Math.Min((Bounds.Width - 24) / _image.Width, (Bounds.Height - 64) / _image.Height);
        var scale = fit >= 1 ? Math.Floor(fit) : Math.Max(0.1, fit);
        var origin = new Point(Math.Round((Bounds.Width - _image.Width * scale) / 2), Math.Round((Bounds.Height - _image.Height * scale) / 2));
        return (scale, origin);
    }

    /// <summary>Where a point in sprite pixels appears on this control.</summary>
    public Point ToControl(double x, double y)
    {
        var (scale, o) = Layout();
        return new Point(o.X + x * scale, o.Y + y * scale);
    }

    (int x, int y)? PixelAt(Point p)
    {
        var (scale, o) = Layout();
        var x = (int)Math.Floor((p.X - o.X) / scale);
        var y = (int)Math.Floor((p.Y - o.Y) / scale);
        return x >= 0 && y >= 0 && x < _image.Width && y < _image.Height ? (x, y) : null;
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(UiTheme.Background, new Rect(Bounds.Size));
        var (scale, o) = Layout();
        var rect = new Rect(o.X, o.Y, _image.Width * scale, _image.Height * scale);

        // transparency checkerboard
        var a = new SolidColorBrush(Avalonia.Media.Color.Parse("#2A2E36"));
        var b = new SolidColorBrush(Avalonia.Media.Color.Parse("#22252C"));
        const double check = 8;
        using (ctx.PushClip(rect))
            for (var y = rect.Y; y < rect.Bottom; y += check)
                for (var x = rect.X; x < rect.Right; x += check)
                    ctx.FillRectangle(((int)((x - rect.X) / check) + (int)((y - rect.Y) / check)) % 2 == 0 ? a : b, new Rect(x, y, check, check));

        using (ctx.PushRenderOptions(Pixelated))
        {
            if (Onion is { } onion && onion.Width == _image.Width && onion.Height == _image.Height)
                using (ctx.PushOpacity(0.28))
                    ctx.DrawImage(SpriteCache.Get(onion), new Rect(0, 0, onion.Width, onion.Height), rect);
            ctx.DrawImage(SpriteCache.Get(_image), new Rect(0, 0, _image.Width, _image.Height), rect);
        }

        if (scale >= 6)
        {
            var pen = new Pen(new SolidColorBrush(Colors.White, 0.07), 1);
            for (var x = 1; x < _image.Width; x++) ctx.DrawLine(pen, new Point(rect.X + x * scale + 0.5, rect.Y), new Point(rect.X + x * scale + 0.5, rect.Bottom));
            for (var y = 1; y < _image.Height; y++) ctx.DrawLine(pen, new Point(rect.X, rect.Y + y * scale + 0.5), new Point(rect.Right, rect.Y + y * scale + 0.5));
        }
        if (Mirror)
        {
            var mx = rect.X + rect.Width / 2;
            ctx.DrawLine(new Pen(UiTheme.Accent, 1) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) }, new Point(mx, rect.Y - 8), new Point(mx, rect.Bottom + 8));
        }
        ctx.DrawRectangle(null, new Pen(UiTheme.Border, 1), rect.Inflate(0.5));

        if (BoxOwner is not null) DrawBox(ctx, BoxRect(scale, o));

        if (_hover is { } h && Tool != PixelTool.Collision)
        {
            var cell = new Rect(rect.X + h.x * scale, rect.Y + h.y * scale, scale, scale);
            ctx.DrawRectangle(null, new Pen(Brushes.White, 1), cell);
        }
    }

    // ------------------------------------------------------------------ collision box

    static readonly IBrush BoxBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#FF4FD8"));

    Rect BoxRect(double scale, Point o)
    {
        var b = BoxOwner!.Box;
        return new Rect(o.X + b.X * scale, o.Y + b.Y * scale, b.W * scale, b.H * scale);
    }

    static IEnumerable<(Point p, bool l, bool r, bool t, bool b)> Handles(Rect r) => new[]
    {
        (r.TopLeft, true, false, true, false), (new Point(r.Center.X, r.Top), false, false, true, false),
        (r.TopRight, false, true, true, false), (new Point(r.Right, r.Center.Y), false, true, false, false),
        (r.BottomRight, false, true, false, true), (new Point(r.Center.X, r.Bottom), false, false, false, true),
        (r.BottomLeft, true, false, false, true), (new Point(r.Left, r.Center.Y), true, false, false, false),
    };

    void DrawBox(DrawingContext ctx, Rect r)
    {
        var editing = Tool == PixelTool.Collision;
        ctx.FillRectangle(new SolidColorBrush(Avalonia.Media.Color.Parse("#FF4FD8"), editing ? 0.07 : 0.0), r);
        var pen = editing ? new Pen(BoxBrush, 2) : new Pen(BoxBrush, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        ctx.DrawRectangle(null, pen, r);
        if (!editing) return;
        foreach (var h in Handles(r))
            ctx.DrawRectangle(Brushes.White, new Pen(BoxBrush, 2), new Rect(h.p.X - HandleSize / 2, h.p.Y - HandleSize / 2, HandleSize, HandleSize), 2, 2);
        // size readout in the canvas corner, where it never covers the art
        var b = BoxOwner!.Box;
        var label = SceneRenderer.Text($"Collision box  {b.X}, {b.Y}  ·  {b.W} × {b.H}", 11, Brushes.White, FontWeight.SemiBold);
        var tag = new Rect(10, 8, label.Width + 12, label.Height + 6);
        ctx.DrawRectangle(BoxBrush, null, tag, 4, 4);
        ctx.DrawText(label, new Point(tag.X + 6, tag.Y + 3));
    }

    /// <summary>Nearest pixel grid line (0..size) under a screen point.</summary>
    (int X, int Y) GridAt(Point p)
    {
        var (scale, o) = Layout();
        return ((int)Math.Clamp(Math.Round((p.X - o.X) / scale), 0, _image.Width),
                (int)Math.Clamp(Math.Round((p.Y - o.Y) / scale), 0, _image.Height));
    }

    void StartBoxDrag(Point p)
    {
        var (scale, o) = Layout();
        var r = BoxRect(scale, o);
        _boxStart = BoxOwner!.Box;
        _gridStart = GridAt(p);
        _boxSnapshotTaken = false;
        _dragLeft = _dragRight = _dragTop = _dragBottom = false;

        foreach (var h in Handles(r))
            if (Math.Abs(p.X - h.p.X) <= HandleSize && Math.Abs(p.Y - h.p.Y) <= HandleSize)
            {
                (_dragLeft, _dragRight, _dragTop, _dragBottom) = (h.l, h.r, h.t, h.b);
                _boxDrag = BoxDrag.Resize;
                return;
            }
        _boxDrag = r.Contains(p) ? BoxDrag.Move : BoxDrag.Draw;
    }

    void ContinueBoxDrag(Point p)
    {
        var g = GridAt(p);
        var s = _boxStart;
        int x = s.X, y = s.Y, w = s.W, h = s.H;
        switch (_boxDrag)
        {
            case BoxDrag.Move:
                x = Math.Clamp(s.X + g.X - _gridStart.X, 0, _image.Width - s.W);
                y = Math.Clamp(s.Y + g.Y - _gridStart.Y, 0, _image.Height - s.H);
                break;
            case BoxDrag.Resize:
                int left = s.X, top = s.Y, right = s.X + s.W, bottom = s.Y + s.H;
                if (_dragLeft) left = Math.Min(g.X, right - 1);
                if (_dragRight) right = Math.Max(g.X, left + 1);
                if (_dragTop) top = Math.Min(g.Y, bottom - 1);
                if (_dragBottom) bottom = Math.Max(g.Y, top + 1);
                (x, y, w, h) = (left, top, right - left, bottom - top);
                break;
            case BoxDrag.Draw:
                x = Math.Min(g.X, _gridStart.X);
                y = Math.Min(g.Y, _gridStart.Y);
                w = Math.Max(1, Math.Abs(g.X - _gridStart.X));
                h = Math.Max(1, Math.Abs(g.Y - _gridStart.Y));
                break;
        }
        if ((x, y, w, h) == BoxOwner!.Box) return;
        if (!_boxSnapshotTaken) { BoxChanging?.Invoke(); _boxSnapshotTaken = true; }
        BoxOwner.SetBox(x, y, w, h);
        InvalidateVisual();
        BoxChanged?.Invoke();
    }

    void UpdateBoxCursor(Point p)
    {
        if (Tool != PixelTool.Collision || BoxOwner is null) { Cursor = new Cursor(StandardCursorType.Cross); return; }
        var (scale, o) = Layout();
        var r = BoxRect(scale, o);
        foreach (var h in Handles(r))
            if (Math.Abs(p.X - h.p.X) <= HandleSize && Math.Abs(p.Y - h.p.Y) <= HandleSize)
            {
                Cursor = new Cursor((h.l || h.r) && (h.t || h.b)
                    ? (h.l == h.t ? StandardCursorType.TopLeftCorner : StandardCursorType.TopRightCorner)
                    : h.l || h.r ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
                return;
            }
        Cursor = new Cursor(r.Contains(p) ? StandardCursorType.SizeAll : StandardCursorType.Cross);
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (Tool == PixelTool.Collision && BoxOwner is not null && props.IsLeftButtonPressed)
        {
            StartBoxDrag(e.GetPosition(this));
            e.Pointer.Capture(this);
            return;
        }
        if (PixelAt(e.GetPosition(this)) is not { } p) return;
        _erasing = props.IsRightButtonPressed || Tool == PixelTool.Eraser;

        if (Tool == PixelTool.Picker && !props.IsRightButtonPressed)
        {
            var c = _image[p.x, p.y];
            if (c != 0) ColorPicked?.Invoke(c);
            return;
        }

        Changing?.Invoke();
        if (Tool == PixelTool.Fill && !props.IsRightButtonPressed)
        {
            Fill(p.x, p.y, Color);
            if (Mirror) Fill(_image.Width - 1 - p.x, p.y, Color);
            Commit();
            return;
        }
        _drawing = true;
        _last = p;
        Plot(p.x, p.y);
        Commit();
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_boxDrag != BoxDrag.None) { ContinueBoxDrag(e.GetPosition(this)); return; }
        UpdateBoxCursor(e.GetPosition(this));
        var p = PixelAt(e.GetPosition(this));
        if (p != _hover) { _hover = p; InvalidateVisual(); }
        if (!_drawing || p is not { } now) return;
        if (_last is { } last) Line(last.x, last.y, now.x, now.y);
        _last = now;
        Commit();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drawing = false;
        _last = null;
        _boxDrag = BoxDrag.None;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    void Commit()
    {
        SpriteCache.Invalidate(_image);
        InvalidateVisual();
        Changed?.Invoke();
    }

    void Plot(int x, int y)
    {
        var c = _erasing ? 0u : Color;
        _image[x, y] = c;
        if (Mirror) _image[_image.Width - 1 - x, y] = c;
    }

    void Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            Plot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    void Fill(int x, int y, uint color)
    {
        var target = _image[x, y];
        if (target == color) return;
        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= _image.Width || cy >= _image.Height || _image[cx, cy] != target) continue;
            _image[cx, cy] = color;
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
    }
}

/// <summary>
/// Edits an object's animations: draw frames, add and reorder them, name animations,
/// import PNGs and sprite sheets, and watch a live preview.
/// </summary>
public sealed class SpriteEditor : UserControl
{
    static readonly string[] Palette =
    {
        "#1B1D2A", "#3A3F58", "#5A6075", "#8A90A6", "#C9CED9", "#FFFFFF", "#6B2E2E",
        "#C4553A", "#F0524F", "#FF9A92", "#E07AC1", "#8E5BD9", "#4C2A85", "#3E7BFA",
        "#8FB8FF", "#8ED6FF", "#1F6F78", "#2FCB7E", "#3E9E45", "#5CC85A", "#9BEA6A",
        "#FFF1B0", "#FFC93C", "#F5A524", "#D9A066", "#B57B45", "#84492A", "#FFD3A8",
    };

    readonly ObjectType _type;
    readonly PixelCanvas _canvas = new();
    readonly ListBox _animList = new() { Background = Brushes.Transparent, MaxHeight = 220 };
    readonly StackPanel _frames = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    readonly StackPanel _animSettings = new() { Spacing = 10 };
    readonly Image _preview = new() { Stretch = Stretch.Uniform };
    readonly Border _colorSwatch = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(6), BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1) };
    readonly TextBox _colorHex = new() { Width = 96 };
    readonly WrapPanel _usedColors = new();
    readonly Dictionary<PixelTool, Button> _toolButtons = new();
    readonly TextBlock _message = new() { FontSize = 12, Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _timer;
    readonly NumericUpDown _width, _height, _sliceW, _sliceH;
    readonly Stack<UndoState> _undo = new(), _redo = new();
    readonly NumericUpDown _boxX, _boxY, _boxW, _boxH;
    bool _updatingBoxFields;

    sealed record UndoState(List<SpriteAnimation> Animations, int Width, int Height, int BoxX, int BoxY, int BoxW, int BoxH);
    int _anim, _frame;
    bool _onion = true;
    double _previewTime;

    public SpriteEditor(ObjectType type)
    {
        _type = type;
        if (_type.Animations.Count == 0)
            _type.Animations.Add(new SpriteAnimation { Name = "Default", Frames = { new PixelImage(_type.Width, _type.Height) } });

        _width = Ui.Number(type.Width, 1, PixelImage.MaxSize, 1, _ => { });
        _height = Ui.Number(type.Height, 1, PixelImage.MaxSize, 1, _ => { });
        _sliceW = Ui.Number(0, 0, PixelImage.MaxSize, 1, _ => { });
        _sliceH = Ui.Number(0, 0, PixelImage.MaxSize, 1, _ => { });
        _boxX = Ui.Number(0, 0, PixelImage.MaxSize, 1, _ => BoxFieldsChanged());
        _boxY = Ui.Number(0, 0, PixelImage.MaxSize, 1, _ => BoxFieldsChanged());
        _boxW = Ui.Number(1, 1, PixelImage.MaxSize, 1, _ => BoxFieldsChanged());
        _boxH = Ui.Number(1, 1, PixelImage.MaxSize, 1, _ => BoxFieldsChanged());
        foreach (var n in new[] { _width, _height, _sliceW, _sliceH, _boxX, _boxY, _boxW, _boxH }) n.ShowButtonSpinner = false;

        _canvas.Changing += SaveUndo;
        _canvas.Changed += () => { RefreshFrames(); RefreshUsedColors(); Edited?.Invoke(); };
        _canvas.ColorPicked += SetColor;
        _canvas.BoxOwner = _type;
        _canvas.BoxChanging += SaveUndo;
        _canvas.BoxChanged += () => { RefreshBoxFields(); Edited?.Invoke(); };

        var left = BuildAnimationPanel();
        var center = BuildCenter();
        var right = BuildRightPanel();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*,230") };
        grid.Children.Add(left);
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Content = grid;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 30), DispatcherPriority.Background, (_, _) => TickPreview());
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();

        SetTool(PixelTool.Pencil);
        SetColor(0xFF1B1D2A);
        RefreshAnimations();
        RefreshBoxFields();
    }

    /// <summary>Raised after any change to the sprite.</summary>
    public event Action? Edited;

    SpriteAnimation Anim => _type.Animations[Math.Clamp(_anim, 0, _type.Animations.Count - 1)];

    // ------------------------------------------------------------------ layout

    Control BuildAnimationPanel()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(16, 16, 12, 16) };
        stack.Children.Add(Ui.Heading("Animations"));
        _animList.SelectionChanged += (_, _) =>
        {
            if (_animList.SelectedIndex < 0 || _animList.SelectedIndex == _anim) return;
            _anim = _animList.SelectedIndex;
            _frame = 0;
            RefreshAnimationSettings();
            RefreshFrames();
        };
        stack.Children.Add(_animList);

        var add = Ui.IconButton(Ui.Icons.Plus, "Add an animation", AddAnimation, "Add animation");
        stack.Children.Add(add);
        stack.Children.Add(Ui.Label("Platformer and walking objects play Idle, Run, Jump and Fall automatically. Others play Default.", 12, UiTheme.Muted));
        stack.Children.Add(Ui.Divider());
        stack.Children.Add(_animSettings);

        return new Border
        {
            Background = UiTheme.Panel, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Content = stack },
        };
    }

    Control BuildCenter()
    {
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10, 8) };
        void Tool(PixelTool t, string icon, string label, string tip)
        {
            var b = Ui.IconButton(icon, tip, () => SetTool(t), label);
            _toolButtons[t] = b;
            tools.Children.Add(b);
        }
        Tool(PixelTool.Pencil, Ui.Icons.Paint, "Pencil", "Draw pixels (P). Right-click erases.");
        Tool(PixelTool.Eraser, Ui.Icons.Erase, "Eraser", "Erase pixels (E)");
        Tool(PixelTool.Fill, Ui.Icons.Fill, "Fill", "Fill an area with the colour (F)");
        Tool(PixelTool.Picker, Ui.Icons.Picker, "Pick", "Pick a colour from the sprite (I)");
        Tool(PixelTool.Collision, Ui.Icons.Fit, "Collision", "Drag the pink outline to set what counts as touching (C). Drag inside to move it, drag a handle to resize, or drag outside to draw a new box.");
        tools.Children.Add(Ui.Divider(Orientation.Vertical));

        var mirror = new ToggleButton { Content = "Mirror", Padding = new Thickness(10, 5) };
        ToolTip.SetTip(mirror, "Draw on both halves at once");
        mirror.IsCheckedChanged += (_, _) => { _canvas.Mirror = mirror.IsChecked == true; _canvas.InvalidateVisual(); };
        tools.Children.Add(mirror);
        var onion = new ToggleButton { Content = "Onion skin", Padding = new Thickness(10, 5), IsChecked = true };
        ToolTip.SetTip(onion, "Show the previous frame faintly underneath");
        onion.IsCheckedChanged += (_, _) => { _onion = onion.IsChecked == true; ShowFrame(); };
        tools.Children.Add(onion);
        tools.Children.Add(Ui.Divider(Orientation.Vertical));
        tools.Children.Add(Ui.IconButton(Ui.Icons.FlipH, "Flip this frame left to right", () => Transform(img => img.Mirrored())));
        tools.Children.Add(Ui.IconButton(Ui.Icons.Trash, "Clear this frame", () => Transform(img => new PixelImage(img.Width, img.Height))));
        tools.Children.Add(Ui.Divider(Orientation.Vertical));
        tools.Children.Add(Ui.IconButton(Ui.Icons.Undo, "Undo (Ctrl+Z)", Undo));
        tools.Children.Add(Ui.IconButton(Ui.Icons.Redo, "Redo (Ctrl+Y)", Redo));

        var frameBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        frameBar.Children.Add(Ui.IconButton(Ui.Icons.Plus, "Add a blank frame", () => EditFrames(f => f.Insert(_frame + 1, new PixelImage(_type.Width, _type.Height)), +1)));
        frameBar.Children.Add(Ui.IconButton(Ui.Icons.Copy, "Duplicate this frame", () => EditFrames(f => f.Insert(_frame + 1, f[_frame].Clone()), +1)));
        frameBar.Children.Add(Ui.IconButton(Ui.Icons.Left, "Move frame earlier", () => MoveFrame(-1)));
        frameBar.Children.Add(Ui.IconButton(Ui.Icons.Right, "Move frame later", () => MoveFrame(1)));
        frameBar.Children.Add(Ui.IconButton(Ui.Icons.Close, "Delete this frame", () =>
        {
            if (Anim.Frames.Count > 1) EditFrames(f => f.RemoveAt(_frame), -1);
        }));

        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 8) };
        strip.Children.Add(Ui.Heading("Frames"));
        var scroll = new ScrollViewer
        {
            Content = _frames,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(12, 0),
        };
        Grid.SetColumn(scroll, 1);
        strip.Children.Add(scroll);
        Grid.SetColumn(frameBar, 2);
        strip.Children.Add(frameBar);

        var dock = new DockPanel();
        var toolBorder = new Border { Child = tools, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(toolBorder, Dock.Top);
        dock.Children.Add(toolBorder);
        var stripBorder = new Border { Child = strip, Background = UiTheme.Panel, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(stripBorder, Dock.Bottom);
        dock.Children.Add(stripBorder);
        dock.Children.Add(_canvas);
        return dock;
    }

    Control BuildRightPanel()
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(14, 16, 16, 16) };

        stack.Children.Add(Ui.Heading("Preview"));
        stack.Children.Add(new Border
        {
            Height = 120, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.Parse("#8ED6FF")),
            Padding = new Thickness(10), Child = _preview,
        });
        RenderOptions.SetBitmapInterpolationMode(_preview, BitmapInterpolationMode.None);

        stack.Children.Add(Ui.Heading("Colour"));
        var current = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        current.Children.Add(_colorSwatch);
        _colorHex.LostFocus += (_, _) => { if (TryParseColor(_colorHex.Text, out var c)) SetColor(c); };
        _colorHex.KeyDown += (_, e) => { if (e.Key == Key.Enter && TryParseColor(_colorHex.Text, out var c)) SetColor(c); };
        current.Children.Add(_colorHex);
        stack.Children.Add(current);

        var palette = new WrapPanel();
        foreach (var hex in Palette) palette.Children.Add(Swatch(ParseHex(hex)));
        stack.Children.Add(palette);
        stack.Children.Add(Ui.Label("Used in this object", 12, UiTheme.Muted));
        stack.Children.Add(_usedColors);

        stack.Children.Add(Ui.Divider());
        stack.Children.Add(Ui.Heading("Size"));
        stack.Children.Add(Ui.Label("The drawing canvas. Art stays anchored to the bottom when resized.", 12, UiTheme.Muted));
        var size = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 8 };
        size.Children.Add(Ui.Field("Width", _width));
        var h = Ui.Field("Height", _height);
        Grid.SetColumn(h, 1);
        size.Children.Add(h);
        stack.Children.Add(size);
        var resize = new Button { Content = "Apply size", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        resize.Click += (_, _) => Resize((int)(_width.Value ?? _type.Width), (int)(_height.Value ?? _type.Height));
        stack.Children.Add(resize);

        stack.Children.Add(Ui.Divider());
        stack.Children.Add(Ui.Heading("Collision box"));
        stack.Children.Add(Ui.Label("What counts as touching: walls, floors and other objects. Use the Collision tool to drag the pink outline.", 12, UiTheme.Muted));
        var boxGrid = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 8 };
        var boxLeft = new StackPanel { Spacing = 8 };
        var boxRight = new StackPanel { Spacing = 8 };
        boxLeft.Children.Add(Ui.Field("X", _boxX));
        boxLeft.Children.Add(Ui.Field("Width", _boxW));
        boxRight.Children.Add(Ui.Field("Y", _boxY));
        boxRight.Children.Add(Ui.Field("Height", _boxH));
        Grid.SetColumn(boxRight, 1);
        boxGrid.Children.Add(boxLeft);
        boxGrid.Children.Add(boxRight);
        stack.Children.Add(boxGrid);
        var boxButtons = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 8 };
        var editBox = new Button { Content = "Edit on canvas", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        editBox.Click += (_, _) => SetTool(PixelTool.Collision);
        var whole = new Button { Content = "Whole sprite", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(whole, "Make the box cover the whole sprite");
        whole.Click += (_, _) =>
        {
            SaveUndo();
            _type.SetBox(0, 0, _type.Width, _type.Height);
            RefreshBoxFields();
            _canvas.InvalidateVisual();
            Edited?.Invoke();
        };
        Grid.SetColumn(whole, 1);
        boxButtons.Children.Add(editBox);
        boxButtons.Children.Add(whole);
        stack.Children.Add(boxButtons);

        stack.Children.Add(Ui.Divider());
        stack.Children.Add(Ui.Heading("Import image"));
        stack.Children.Add(Ui.Label("PNG up to 256 × 256. For a sprite sheet, give the size of one frame; frames are read left to right, top to bottom.", 12, UiTheme.Muted));
        var slice2 = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 8 };
        slice2.Children.Add(Ui.Field("Frame width", _sliceW));
        var sh2 = Ui.Field("Frame height", _sliceH);
        Grid.SetColumn(sh2, 1);
        slice2.Children.Add(sh2);
        stack.Children.Add(slice2);
        var import = new Button { Content = "Import PNG into this animation…", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        import.Click += async (_, _) => await ImportPng();
        stack.Children.Add(import);
        stack.Children.Add(_message);

        return new Border
        {
            Background = UiTheme.Panel, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = stack },
        };
    }

    Button Swatch(uint color)
    {
        var b = new Button
        {
            Width = 24, Height = 24, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(0),
            CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(ToColor(color)),
            BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1),
        };
        ToolTip.SetTip(b, ToHex(color));
        b.Click += (_, _) => SetColor(color);
        return b;
    }

    // ------------------------------------------------------------------ state

    public void SetTool(PixelTool tool)
    {
        _canvas.Tool = tool;
        _canvas.InvalidateVisual();
        foreach (var (t, b) in _toolButtons)
        {
            b.Background = t == tool ? UiTheme.AccentSoft : Brushes.Transparent;
            b.Foreground = t == tool ? UiTheme.Text : UiTheme.Muted;
        }
    }

    void SetColor(uint color)
    {
        _canvas.Color = color;
        _colorSwatch.Background = new SolidColorBrush(ToColor(color));
        _colorHex.Text = ToHex(color);
        if (_canvas.Tool is PixelTool.Eraser or PixelTool.Picker) SetTool(PixelTool.Pencil);
    }

    public void HandleKey(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (!ctrl && e.Key == Key.P) SetTool(PixelTool.Pencil);
        else if (!ctrl && e.Key == Key.E) SetTool(PixelTool.Eraser);
        else if (!ctrl && e.Key == Key.F) SetTool(PixelTool.Fill);
        else if (!ctrl && e.Key == Key.I) SetTool(PixelTool.Picker);
        else if (!ctrl && e.Key == Key.C) SetTool(PixelTool.Collision);
        else if (!ctrl && e.Key == Key.Left) { _frame = Math.Max(0, _frame - 1); RefreshFrames(); }
        else if (!ctrl && e.Key == Key.Right) { _frame = Math.Min(Anim.Frames.Count - 1, _frame + 1); RefreshFrames(); }
    }

    UndoState Capture() => new(_type.Animations.Select(a => a.Clone()).ToList(), _type.Width, _type.Height,
        _type.CollisionX, _type.CollisionY, _type.CollisionWidth, _type.CollisionHeight);

    void SaveUndo()
    {
        _undo.Push(Capture());
        if (_undo.Count > 100) { var keep = _undo.Take(100).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
        _redo.Clear();
    }

    void Undo() => Swap(_undo, _redo);
    void Redo() => Swap(_redo, _undo);

    void Swap(Stack<UndoState> from, Stack<UndoState> to)
    {
        if (from.Count == 0) return;
        to.Push(Capture());
        var s = from.Pop();
        _type.Animations = s.Animations;
        (_type.Width, _type.Height) = (s.Width, s.Height);
        (_type.CollisionX, _type.CollisionY, _type.CollisionWidth, _type.CollisionHeight) = (s.BoxX, s.BoxY, s.BoxW, s.BoxH);
        _width.Value = s.Width;
        _height.Value = s.Height;
        RefreshAnimations();
        RefreshBoxFields();
        Edited?.Invoke();
    }

    void RefreshBoxFields()
    {
        _updatingBoxFields = true;
        var b = _type.Box;
        _boxX.Maximum = _type.Width - 1;
        _boxY.Maximum = _type.Height - 1;
        _boxW.Maximum = _type.Width;
        _boxH.Maximum = _type.Height;
        (_boxX.Value, _boxY.Value, _boxW.Value, _boxH.Value) = (b.X, b.Y, b.W, b.H);
        _updatingBoxFields = false;
    }

    void BoxFieldsChanged()
    {
        if (_updatingBoxFields) return;
        var box = ((int)(_boxX.Value ?? 0), (int)(_boxY.Value ?? 0), (int)(_boxW.Value ?? 1), (int)(_boxH.Value ?? 1));
        if (box == _type.Box) return;
        SaveUndo();
        _type.SetBox(box.Item1, box.Item2, box.Item3, box.Item4);
        _canvas.InvalidateVisual();
        Edited?.Invoke();
    }

    void Changed()
    {
        RefreshBoxFields();
        RefreshFrames();
        RefreshUsedColors();
        Edited?.Invoke();
    }

    // ------------------------------------------------------------------ animations

    void RefreshAnimations()
    {
        _anim = Math.Clamp(_anim, 0, _type.Animations.Count - 1);
        _animList.ItemsSource = _type.Animations.Select(a => $"{a.Name}   ·  {a.Frames.Count}").ToList();
        _animList.SelectedIndex = _anim;
        RefreshAnimationSettings();
        RefreshFrames();
        RefreshUsedColors();
    }

    void RefreshAnimationSettings()
    {
        _animSettings.Children.Clear();
        var anim = Anim;

        var suggestions = new[] { "Default", "Idle", "Run", "Jump", "Fall", "Walk", "Hurt", "Attack" };
        var name = new AutoCompleteBox { Text = anim.Name, ItemsSource = suggestions, FilterMode = AutoCompleteFilterMode.StartsWith, MinimumPrefixLength = 0 };
        name.LostFocus += (_, _) =>
        {
            var n = (name.Text ?? "").Trim();
            if (n.Length == 0 || n == anim.Name) return;
            SaveUndo();
            anim.Name = n;
            RefreshAnimations();
            Edited?.Invoke();
        };
        _animSettings.Children.Add(Ui.Field("Name", name));
        _animSettings.Children.Add(Ui.Field("Frames per second", Ui.Number(anim.Fps, 0, 60, 1, v => { anim.Fps = v; Edited?.Invoke(); })));
        var loop = new CheckBox { Content = "Loop", IsChecked = anim.Loop };
        loop.IsCheckedChanged += (_, _) => { anim.Loop = loop.IsChecked == true; Edited?.Invoke(); };
        _animSettings.Children.Add(loop);
        var delete = new Button { Content = "Delete animation", Foreground = UiTheme.Danger, IsEnabled = _type.Animations.Count > 1 };
        delete.Click += (_, _) =>
        {
            SaveUndo();
            _type.Animations.RemoveAt(_anim);
            _anim = Math.Max(0, _anim - 1);
            RefreshAnimations();
            Edited?.Invoke();
        };
        _animSettings.Children.Add(delete);
    }

    void AddAnimation()
    {
        SaveUndo();
        var names = new[] { "Idle", "Run", "Jump", "Fall", "Walk", "Hurt" };
        var name = names.FirstOrDefault(n => _type.Animation(n) is null && _type.Animations.All(a => a.Name != n)) ?? $"Animation {_type.Animations.Count + 1}";
        // start from a copy of the current frame so there is something to edit
        _type.Animations.Add(new SpriteAnimation { Name = name, Fps = 8, Frames = { Anim.Frames[Math.Min(_frame, Anim.Frames.Count - 1)].Clone() } });
        _anim = _type.Animations.Count - 1;
        _frame = 0;
        RefreshAnimations();
        Edited?.Invoke();
    }

    // ------------------------------------------------------------------ frames

    void RefreshFrames()
    {
        var frames = Anim.Frames;
        if (frames.Count == 0) frames.Add(new PixelImage(_type.Width, _type.Height));
        _frame = Math.Clamp(_frame, 0, frames.Count - 1);
        _frames.Children.Clear();
        for (var i = 0; i < frames.Count; i++)
        {
            var index = i;
            var img = new Image { Source = SpriteCache.Get(frames[i]), Width = 42, Height = 42, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
            var cell = new StackPanel { Spacing = 2 };
            cell.Children.Add(new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(6),
                Background = UiTheme.Surface,
                BorderBrush = i == _frame ? UiTheme.Accent : UiTheme.Border,
                BorderThickness = new Thickness(i == _frame ? 2 : 1),
                Child = img,
                Cursor = new Cursor(StandardCursorType.Hand),
            });
            cell.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 10, Foreground = UiTheme.Muted, HorizontalAlignment = HorizontalAlignment.Center });
            cell.PointerPressed += (_, _) => { _frame = index; RefreshFrames(); };
            _frames.Children.Add(cell);
        }
        _animList.ItemsSource = _type.Animations.Select(a => $"{a.Name}   ·  {a.Frames.Count}").ToList();
        _animList.SelectedIndex = _anim;
        ShowFrame();
    }

    void ShowFrame()
    {
        var frames = Anim.Frames;
        _canvas.Image = frames[_frame];
        _canvas.Onion = _onion && _frame > 0 ? frames[_frame - 1] : null;
        _canvas.InvalidateVisual();
    }

    void EditFrames(Action<List<PixelImage>> change, int moveSelection)
    {
        SaveUndo();
        change(Anim.Frames);
        _frame += moveSelection;
        Changed();
    }

    void MoveFrame(int by)
    {
        var to = _frame + by;
        if (to < 0 || to >= Anim.Frames.Count) return;
        EditFrames(f => (f[_frame], f[to]) = (f[to], f[_frame]), by);
    }

    void Transform(Func<PixelImage, PixelImage> f)
    {
        SaveUndo();
        Anim.Frames[_frame] = f(Anim.Frames[_frame]);
        Changed();
    }

    void Resize(int w, int h)
    {
        if (w == _type.Width && h == _type.Height) return;
        SaveUndo();
        _type.Resize(w, h);
        _message.Text = $"Resized to {_type.Width} × {_type.Height}.";
        Changed();
    }

    void RefreshUsedColors()
    {
        _usedColors.Children.Clear();
        var used = _type.Animations.SelectMany(a => a.Frames).SelectMany(f => f.Pixels)
            .Where(p => p != 0).GroupBy(p => p).OrderByDescending(g => g.Count()).Take(14).Select(g => g.Key);
        foreach (var c in used) _usedColors.Children.Add(Swatch(c));
    }

    void TickPreview()
    {
        var anim = Anim;
        if (anim.Frames.Count == 0) return;
        _previewTime += 1.0 / 30;
        var index = anim.Fps <= 0 ? _frame : (int)(_previewTime * anim.Fps) % anim.Frames.Count;
        if (!anim.Loop && anim.Fps > 0) index = Math.Min((int)(_previewTime * anim.Fps) % (anim.Frames.Count + 4), anim.Frames.Count - 1);
        _preview.Source = SpriteCache.Get(anim.Frames[Math.Clamp(index, 0, anim.Frames.Count - 1)]);
    }

    // ------------------------------------------------------------------ import

    async Task ImportPng()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a PNG",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            var image = LoadPng(path);
            var count = ImportImage(image, (int)(_sliceW.Value ?? 0), (int)(_sliceH.Value ?? 0));
            _message.Text = $"Imported {count} frame{(count == 1 ? "" : "s")} from {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            _message.Text = $"Could not import {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    /// <summary>Replaces the current animation's frames with an image, sliced into frames if asked.</summary>
    public int ImportImage(PixelImage image, int frameW, int frameH)
    {
        if (frameW <= 0 || frameW > image.Width) frameW = image.Width;
        if (frameH <= 0 || frameH > image.Height) frameH = image.Height;
        if (frameW > PixelImage.MaxSize || frameH > PixelImage.MaxSize)
            throw new InvalidOperationException($"frames can be at most {PixelImage.MaxSize} × {PixelImage.MaxSize} pixels. Set a frame size to slice a sprite sheet.");

        var frames = new List<PixelImage>();
        for (var y = 0; y + frameH <= image.Height; y += frameH)
            for (var x = 0; x + frameW <= image.Width; x += frameW)
            {
                var f = new PixelImage(frameW, frameH);
                for (var j = 0; j < frameH; j++)
                    for (var i = 0; i < frameW; i++)
                        f[i, j] = image[x + i, y + j];
                if (f.Pixels.Any(p => p != 0)) frames.Add(f);
            }
        if (frames.Count == 0) throw new InvalidOperationException("the image is empty.");

        SaveUndo();
        if (frameW != _type.Width || frameH != _type.Height) _type.Resize(frameW, frameH);
        Anim.Frames.Clear();
        Anim.Frames.AddRange(frames);
        _frame = 0;
        _width.Value = _type.Width;
        _height.Value = _type.Height;
        Changed();
        return frames.Count;
    }

    /// <summary>Reads a PNG into straight-alpha pixels, whatever format the platform decodes to.</summary>
    public static PixelImage LoadPng(string path)
    {
        using var bitmap = new Bitmap(path);
        var size = bitmap.PixelSize;
        if (size.Width > 2048 || size.Height > 2048) throw new InvalidOperationException("the image is larger than 2048 × 2048.");

        // draw it 1:1 into a target with a known format
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
            ctx.DrawImage(bitmap, new Rect(0, 0, size.Width, size.Height), new Rect(0, 0, size.Width, size.Height));

        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { target.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), buffer.Length, stride); }
        finally { handle.Free(); }

        var img = new PixelImage(size.Width, size.Height);
        for (var i = 0; i < img.Pixels.Length; i++)
        {
            int b = buffer[i * 4], g = buffer[i * 4 + 1], r = buffer[i * 4 + 2], a = buffer[i * 4 + 3];
            if (a == 0) continue;
            if (a < 255) { r = Math.Min(255, r * 255 / a); g = Math.Min(255, g * 255 / a); b = Math.Min(255, b * 255 / a); }
            img.Pixels[i] = (uint)(a << 24 | r << 16 | g << 8 | b);
        }
        return img;
    }

    // ------------------------------------------------------------------ colour helpers

    static uint ParseHex(string hex) => 0xFF000000 | uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber);

    static bool TryParseColor(string? text, out uint color)
    {
        color = 0;
        var t = (text ?? "").Trim().TrimStart('#');
        if (t.Length == 6 && uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) { color = 0xFF000000 | rgb; return true; }
        if (t.Length == 8 && uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb)) { color = argb; return true; }
        return false;
    }

    static string ToHex(uint c) => (c >> 24) == 0xFF ? $"#{c & 0xFFFFFF:X6}" : $"#{c:X8}";
    static Color ToColor(uint c) => Color.FromUInt32(c);
}
