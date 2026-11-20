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
/// The premade objects, grouped by category. Drag one into the level, or click it to
/// pick it as the paint brush.
/// </summary>
public sealed class ObjectLibrary : UserControl
{
    readonly Dictionary<string, Border> _tiles = new();
    string? _selected;

    public ObjectLibrary()
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(14, 12, 14, 16) };
        stack.Children.Add(Ui.Label("Objects", 15, weight: FontWeight.SemiBold));
        stack.Children.Add(Ui.Label("Drag into the level, or click to paint.", 12, UiTheme.Muted));

        foreach (var category in ObjectCatalog.Categories)
        {
            var defs = ObjectCatalog.All.Where(d => d.Category == category).ToList();
            if (defs.Count == 0) continue;
            var heading = Ui.Heading(category);
            heading.Margin = new Thickness(0, 12, 0, 2);
            stack.Children.Add(heading);

            var grid = new UniformGrid { Columns = 3 };
            foreach (var def in defs) grid.Children.Add(Tile(def));
            stack.Children.Add(grid);
        }

        Content = new ScrollViewer { Content = stack };
    }

    /// <summary>Raised when an object is clicked, to use it as the paint brush.</summary>
    public event Action<string>? Picked;

    public void Highlight(string? type)
    {
        _selected = type;
        foreach (var (id, tile) in _tiles)
        {
            tile.BorderBrush = id == type ? UiTheme.Accent : Brushes.Transparent;
            tile.Background = id == type ? UiTheme.AccentSoft : Brushes.Transparent;
        }
    }

    Control Tile(ObjectDef def)
    {
        // fit the sprite into a 40px box, keeping whole-pixel scaling
        var scale = Math.Max(1, Math.Floor(40.0 / Math.Max(def.Width, def.Height)));
        var image = new Image
        {
            Source = SpriteCache.Get(def.Id),
            Width = def.Width * scale,
            Height = def.Height * scale,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.None);

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new Border
        {
            Height = 48,
            Background = UiTheme.Surface,
            CornerRadius = new CornerRadius(6),
            Child = image,
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
        ToolTip.SetTip(tile, $"{def.Name}\n{def.Description}");
        _tiles[def.Id] = tile;

        Point? pressedAt = null;
        tile.PointerEntered += (_, _) => { if (_selected != def.Id) tile.Background = UiTheme.SurfaceHover; };
        tile.PointerExited += (_, _) => { if (_selected != def.Id) tile.Background = Brushes.Transparent; };
        tile.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) pressedAt = e.GetPosition(tile);
        };
        tile.PointerMoved += async (_, e) =>
        {
            if (pressedAt is not { } start) return;
            var d = e.GetPosition(tile) - start;
            if (Math.Abs(d.X) + Math.Abs(d.Y) < 5) return;
            pressedAt = null;
            var data = new DataObject();
            data.Set(LevelCanvas.DragFormat, def.Id);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        };
        tile.PointerReleased += (_, _) =>
        {
            if (pressedAt is null) return;
            pressedAt = null;
            Picked?.Invoke(def.Id);
        };
        return tile;
    }
}
