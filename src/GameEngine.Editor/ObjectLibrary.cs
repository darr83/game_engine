using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

/// <summary>
/// The project's objects, grouped by category. Drag one into the level, click it to paint
/// with it, double-click to edit its sprite and behaviour.
/// </summary>
public sealed class ObjectLibrary : UserControl
{
    readonly Dictionary<string, Border> _tiles = new();
    readonly StackPanel _groups = new() { Spacing = 6 };
    Project _project = new();
    string? _selected;

    public ObjectLibrary()
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(14, 12, 14, 16) };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Ui.Label("Objects", 15, weight: FontWeight.SemiBold));
        var add = Ui.IconButton(Ui.Icons.Plus, "Make a new object with your own sprite and behaviour", () => NewRequested?.Invoke(), "New");
        add.Padding = new Thickness(8, 4);
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        stack.Children.Add(header);
        stack.Children.Add(Ui.Label("Drag into the level, click to paint, double-click to edit.", 12, UiTheme.Muted));
        stack.Children.Add(_groups);

        Content = new ScrollViewer { Content = stack };
    }

    /// <summary>Raised when an object is clicked, to use it as the paint brush.</summary>
    public event Action<string>? Picked;
    public event Action<string>? EditRequested;
    public event Action<string>? DuplicateRequested;
    public event Action<string>? DeleteRequested;
    public event Action? NewRequested;

    public void Show(Project project)
    {
        _project = project;
        _tiles.Clear();
        _groups.Children.Clear();

        // premade categories first, in their usual order, then the user's own
        var categories = BuiltInObjects.Categories
            .Concat(project.Objects.Select(o => o.Category))
            .Distinct()
            .Where(c => project.Objects.Any(o => o.Category == c));

        foreach (var category in categories)
        {
            var heading = Ui.Heading(category);
            heading.Margin = new Thickness(0, 12, 0, 2);
            _groups.Children.Add(heading);

            var grid = new UniformGrid { Columns = 3 };
            foreach (var def in project.Objects.Where(d => d.Category == category)) grid.Children.Add(Tile(def));
            _groups.Children.Add(grid);
        }
        Highlight(_selected);
    }

    public void Highlight(string? type)
    {
        _selected = type;
        foreach (var (id, tile) in _tiles)
        {
            tile.BorderBrush = id == type ? UiTheme.Accent : Brushes.Transparent;
            tile.Background = id == type ? UiTheme.AccentSoft : Brushes.Transparent;
        }
    }

    /// <summary>A pixel-perfect thumbnail of an object's first frame.</summary>
    public static Image Thumbnail(ObjectType def, double box)
    {
        var scale = Math.Max(1, Math.Floor(box / Math.Max(def.Width, def.Height)));
        if (Math.Max(def.Width, def.Height) * scale > box) scale = box / Math.Max(def.Width, def.Height);
        var image = new Image
        {
            Source = SpriteCache.Get(def.Icon),
            Width = def.Width * scale,
            Height = def.Height * scale,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        return image;
    }

    Control Tile(ObjectType def)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new Border
        {
            Height = 48,
            Background = UiTheme.Surface,
            CornerRadius = new CornerRadius(6),
            Child = Thumbnail(def, 40),
        });
        content.Children.Add(new TextBlock
        {
            Text = def.Name,
            FontSize = 11,
            Foreground = UiTheme.Text,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var tile = new Border
        {
            Child = content,
            Padding = new Thickness(4),
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(tile, string.IsNullOrWhiteSpace(def.Description) ? def.Name : $"{def.Name}\n{def.Description}");
        _tiles[def.Id] = tile;

        var id = def.Id;
        var inUse = _project.Levels.Any(l => l.Instances.Any(i => i.Type == id));
        tile.ContextMenu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                MenuItem("Edit sprite and behaviour…", () => EditRequested?.Invoke(id)),
                MenuItem("Duplicate", () => DuplicateRequested?.Invoke(id)),
                new Separator(),
                MenuItem(inUse ? "Delete (and remove it from levels)" : "Delete", () => DeleteRequested?.Invoke(id)),
            },
        };

        Point? pressedAt = null;
        tile.PointerEntered += (_, _) => { if (_selected != id) tile.Background = UiTheme.SurfaceHover; };
        tile.PointerExited += (_, _) => { if (_selected != id) tile.Background = Brushes.Transparent; };
        tile.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2)
            {
                pressedAt = null;
                EditRequested?.Invoke(id);
                return;
            }
            pressedAt = e.GetPosition(tile);
        };
        tile.PointerMoved += async (_, e) =>
        {
            if (pressedAt is not { } start) return;
            var d = e.GetPosition(tile) - start;
            if (Math.Abs(d.X) + Math.Abs(d.Y) < 5) return;
            pressedAt = null;
            var data = new DataObject();
            data.Set(LevelCanvas.DragFormat, id);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        };
        tile.PointerReleased += (_, _) =>
        {
            if (pressedAt is null) return;
            pressedAt = null;
            Picked?.Invoke(id);
        };
        return tile;
    }

    static MenuItem MenuItem(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }
}
