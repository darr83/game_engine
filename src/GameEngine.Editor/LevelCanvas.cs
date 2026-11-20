using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

public enum Tool { Select, Paint, Erase }

/// <summary>
/// The level view: place, move and delete objects; paint tiles; pan and zoom.
/// Objects dragged from the object library are dropped here.
/// </summary>
public sealed class LevelCanvas : Control
{
    public const string DragFormat = "application/x-gameengine-object";

    Project _project;
    Level _level;
    double _zoom = 3;
    Point _pan = new(-16, -16);           // world coordinate at the top-left of the view
    bool _needsFit = true;

    // interaction state
    bool _panning, _dragging, _stroking, _strokeErases, _spaceHeld;
    Point _lastScreen;
    Vector _dragOffset;
    Point? _hoverWorld;
    string? _dropType;                    // object being dragged in from the library

    public LevelCanvas(Project project, Level level)
    {
        _project = project;
        _level = level;
        Focusable = true;
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => { _dropType = null; InvalidateVisual(); });
    }

    /// <summary>Raised just before the level is modified, so the editor can save an undo step.</summary>
    public event Action? Changing;
    public event Action? Changed;
    public event Action? SelectionChanged;
    public event Action<string>? StatusChanged;

    public Tool Tool { get; set; } = Tool.Select;
    public string PaintType { get; set; } = "Grass";
    public Instance? Selected { get; private set; }
    int TileSize => _project.TileSize;

    public void SetLevel(Project project, Level level)
    {
        _project = project;
        _level = level;
        Selected = null;
        _needsFit = true;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public void Select(Instance? inst)
    {
        Selected = inst;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public void FitToView()
    {
        if (Bounds.Width < 10 || Bounds.Height < 10) { _needsFit = true; return; }
        var h = _level.Height * TileSize;
        _zoom = Math.Clamp((Bounds.Height - 32) / h, 0.5, 8);
        _pan = new Point(-16 / _zoom, -16 / _zoom);
        _needsFit = false;
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (Selected is null) return;
        Changing?.Invoke();
        _level.Instances.Remove(Selected);
        Select(null);
        Changed?.Invoke();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_needsFit) FitToView();
    }

    // ------------------------------------------------------------------ coordinates

    Point ToWorld(Point screen) => new(screen.X / _zoom + _pan.X, screen.Y / _zoom + _pan.Y);

    /// <summary>Where an object of this type goes when placed in the tile under a point.</summary>
    Point PlacementFor(ObjectDef def, Point world)
    {
        var cx = Math.Floor(world.X / TileSize) * TileSize;
        var cy = Math.Floor(world.Y / TileSize) * TileSize;
        var x = cx + Math.Max(0, (TileSize - def.Width) / 2.0);
        if (def.Width > TileSize) x = cx;
        // platforms hang from the top of the tile; everything else stands on its bottom
        var y = def.Has(Behaviour.JumpThru) || def.Has(Behaviour.MovingPlatform) ? cy : cy + TileSize - def.Height;
        return new Point(Math.Round(x), Math.Round(y));
    }

    Instance? HitTest(Point world)
    {
        return _level.Instances
            .Select(i => (inst: i, def: ObjectCatalog.Find(i.Type)))
            .Where(t => t.def is not null)
            .OrderByDescending(t => SceneRenderer.Layer(t.def!))
            .ThenByDescending(t => _level.Instances.IndexOf(t.inst))
            .Where(t => world.X >= t.inst.X && world.X < t.inst.X + t.def!.Width && world.Y >= t.inst.Y && world.Y < t.inst.Y + t.def.Height)
            .Select(t => t.inst)
            .FirstOrDefault();
    }

    // ------------------------------------------------------------------ rendering

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0C0E11")), bounds);

        double lw = _level.Width * TileSize, lh = _level.Height * TileSize;
        var px = 1 / _zoom;

        using (ctx.PushClip(bounds))
        using (ctx.PushTransform(Matrix.CreateTranslation(-_pan.X, -_pan.Y) * Matrix.CreateScale(_zoom, _zoom)))
        {
            var sky = SceneRenderer.ParseColor(_level.Background, Colors.SkyBlue);
            ctx.FillRectangle(new SolidColorBrush(sky), new Rect(0, 0, lw, lh));

            // tile grid
            var gridPen = new Pen(new SolidColorBrush(Colors.Black, 0.10), px);
            for (var x = 0.0; x <= lw; x += TileSize) ctx.DrawLine(gridPen, new Point(x, 0), new Point(x, lh));
            for (var y = 0.0; y <= lh; y += TileSize) ctx.DrawLine(gridPen, new Point(0, y), new Point(lw, y));

            foreach (var inst in _level.Instances
                         .Select(i => (inst: i, def: ObjectCatalog.Find(i.Type)))
                         .Where(t => t.def is not null)
                         .OrderBy(t => SceneRenderer.Layer(t.def!)))
            {
                var def = inst.def!;
                if (def.Has(Behaviour.MovingPlatform)) DrawPlatformPath(ctx, inst.inst, def, px);
                SceneRenderer.DrawSprite(ctx, def.Id, 0, false, inst.inst.X, inst.inst.Y, def.Width, def.Height);
            }

            // level edge
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Colors.White, 0.35), px), new Rect(0, 0, lw, lh));

            // selection
            if (Selected is not null && ObjectCatalog.Find(Selected.Type) is { } sd)
            {
                var r = new Rect(Selected.X, Selected.Y, sd.Width, sd.Height).Inflate(1.5 * px);
                ctx.DrawRectangle(null, new Pen(UiTheme.Accent, 2 * px), r);
                foreach (var corner in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight })
                    ctx.DrawRectangle(Brushes.White, new Pen(UiTheme.Accent, 1.5 * px), new Rect(corner.X - 3 * px, corner.Y - 3 * px, 6 * px, 6 * px));
            }

            // paint preview
            var previewType = _dropType ?? (Tool == Tool.Paint ? PaintType : null);
            if (_hoverWorld is { } hw && previewType is not null && ObjectCatalog.Find(previewType) is { } pd)
            {
                var at = PlacementFor(pd, hw);
                SceneRenderer.DrawSprite(ctx, pd.Id, 0, false, at.X, at.Y, pd.Width, pd.Height, 0.55);
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Colors.White, 0.8), px), new Rect(at.X, at.Y, pd.Width, pd.Height));
            }
            else if (_hoverWorld is { } ew && Tool == Tool.Erase && HitTest(ew) is { } target && ObjectCatalog.Find(target.Type) is { } td)
            {
                ctx.FillRectangle(new SolidColorBrush(UiTheme.DangerColor, 0.35), new Rect(target.X, target.Y, td.Width, td.Height));
            }
        }
    }

    void DrawPlatformPath(DrawingContext ctx, Instance inst, ObjectDef def, double px)
    {
        double dx = inst.Get(def, "DistanceX"), dy = inst.Get(def, "DistanceY");
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return;
        var from = new Point(inst.X + def.Width / 2.0, inst.Y + def.Height / 2.0);
        var to = new Point(from.X + dx, from.Y + dy);
        var pen = new Pen(new SolidColorBrush(Colors.White, 0.85), 1.5 * px) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        ctx.DrawLine(pen, from, to);
        SceneRenderer.DrawSprite(ctx, def.Id, 0, false, inst.X + dx, inst.Y + dy, def.Width, def.Height, 0.35);
        ctx.DrawEllipse(Brushes.White, null, to, 2.5 * px, 2.5 * px);
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetCurrentPoint(this);
        var world = ToWorld(p.Position);
        _lastScreen = p.Position;

        if (p.Properties.IsMiddleButtonPressed || (p.Properties.IsLeftButtonPressed && _spaceHeld))
        {
            _panning = true;
            e.Pointer.Capture(this);
            return;
        }

        if (p.Properties.IsRightButtonPressed && Tool != Tool.Select)
        {
            BeginStroke(erase: true, world);
            e.Pointer.Capture(this);
            return;
        }

        if (!p.Properties.IsLeftButtonPressed) return;

        switch (Tool)
        {
            case Tool.Select:
                var hit = HitTest(world);
                Select(hit);
                if (hit is not null)
                {
                    Changing?.Invoke();
                    _dragging = true;
                    _dragOffset = new Vector(world.X - hit.X, world.Y - hit.Y);
                    e.Pointer.Capture(this);
                }
                break;
            case Tool.Paint:
                BeginStroke(erase: false, world);
                e.Pointer.Capture(this);
                break;
            case Tool.Erase:
                BeginStroke(erase: true, world);
                e.Pointer.Capture(this);
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var world = ToWorld(pos);
        _hoverWorld = world;
        StatusChanged?.Invoke($"x {Math.Floor(world.X)}   y {Math.Floor(world.Y)}   tile {Math.Floor(world.X / TileSize)}, {Math.Floor(world.Y / TileSize)}");

        if (_panning)
        {
            var d = pos - _lastScreen;
            _pan = new Point(_pan.X - d.X / _zoom, _pan.Y - d.Y / _zoom);
        }
        else if (_dragging && Selected is not null)
        {
            var free = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            var snap = free ? 1.0 : TileSize / 2.0;
            Selected.X = Math.Round((world.X - _dragOffset.X) / snap) * snap;
            Selected.Y = Math.Round((world.Y - _dragOffset.Y) / snap) * snap;
            Changed?.Invoke();
        }
        else if (_stroking)
        {
            ApplyStroke(world);
        }
        _lastScreen = pos;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _panning = _dragging = _stroking = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverWorld = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);
        var before = ToWorld(pos);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), 0.5, 10);
        _pan = new Point(before.X - pos.X / _zoom, before.Y - pos.Y / _zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _spaceHeld = true; e.Handled = true; }
        if (e.Key is Key.Delete or Key.Back) { DeleteSelected(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) _spaceHeld = false;
        base.OnKeyUp(e);
    }

    // ------------------------------------------------------------------ painting

    void BeginStroke(bool erase, Point world)
    {
        Changing?.Invoke();
        _stroking = true;
        _strokeErases = erase;
        ApplyStroke(world);
    }

    void ApplyStroke(Point world)
    {
        if (_strokeErases)
        {
            if (HitTest(world) is { } hit)
            {
                _level.Instances.Remove(hit);
                if (hit == Selected) Select(null);
                Changed?.Invoke();
            }
            return;
        }

        if (ObjectCatalog.Find(PaintType) is not { } def) return;
        var at = PlacementFor(def, world);
        if (at.X < 0 || at.Y < 0 || at.X >= _level.Width * TileSize || at.Y >= _level.Height * TileSize) return;
        if (IsOccupied(def, at)) return;
        _level.Instances.Add(new Instance { Id = _level.NextId(), Type = def.Id, X = at.X, Y = at.Y });
        Changed?.Invoke();
    }

    bool IsOccupied(ObjectDef def, Point at)
    {
        foreach (var i in _level.Instances)
        {
            if (i.Type == def.Id && Math.Abs(i.X - at.X) < 0.5 && Math.Abs(i.Y - at.Y) < 0.5) return true;
            // don't stack solid tiles on top of each other
            if (def.Has(Behaviour.Solid) && ObjectCatalog.Find(i.Type) is { } other && other.Has(Behaviour.Solid) &&
                at.X < i.X + other.Width && at.X + def.Width > i.X && at.Y < i.Y + other.Height && at.Y + def.Height > i.Y)
                return true;
        }
        return false;
    }

    /// <summary>Places a new object (from a drag-drop or a click in the library) under a screen point.</summary>
    public Instance? PlaceAt(string type, Point screen)
    {
        if (ObjectCatalog.Find(type) is not { } def) return null;
        var at = PlacementFor(def, ToWorld(screen));
        Changing?.Invoke();
        var inst = new Instance { Id = _level.NextId(), Type = type, X = at.X, Y = at.Y };
        _level.Instances.Add(inst);
        Select(inst);
        Changed?.Invoke();
        return inst;
    }

    // ------------------------------------------------------------------ drag and drop

    void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Data.Get(DragFormat) is string type)
        {
            _hoverWorld = ToWorld(e.GetPosition(this));
            _dropType = type;
            InvalidateVisual();
        }
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        _dropType = null;
        if (e.Data.Get(DragFormat) is not string type) return;
        PlaceAt(type, e.GetPosition(this));
        Focus();
    }
}
