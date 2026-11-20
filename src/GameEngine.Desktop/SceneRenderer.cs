using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GameEngine.Core;

namespace GameEngine.Desktop;

/// <summary>Turns the engine's pixel art into Avalonia bitmaps, once per frame.</summary>
public static class SpriteCache
{
    static readonly Dictionary<(string, int, bool), Bitmap> Cache = new();

    public static Bitmap Get(string type, int frame = 0, bool mirrored = false)
    {
        var key = (type, frame, mirrored);
        if (Cache.TryGetValue(key, out var bmp)) return bmp;

        var frames = SpriteArt.Frames(type);
        var img = frames[Math.Clamp(frame, 0, frames.Length - 1)];
        if (mirrored) img = img.Mirrored();
        Cache[key] = bmp = ToBitmap(img);
        return bmp;
    }

    public static Bitmap ToBitmap(PixelImage img)
    {
        var bmp = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        var row = new int[img.Width];
        for (var y = 0; y < img.Height; y++)
        {
            for (var x = 0; x < img.Width; x++) row[x] = unchecked((int)img.Pixels[y * img.Width + x]);
            Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, img.Width);
        }
        return bmp;
    }
}

/// <summary>Draws levels and running games. Used by the editor canvas and the game window.</summary>
public static class SceneRenderer
{
    static readonly RenderOptions Pixelated = new() { BitmapInterpolationMode = BitmapInterpolationMode.None, EdgeMode = EdgeMode.Aliased };

    /// <summary>Back-to-front drawing order.</summary>
    public static int Layer(ObjectDef def) =>
        def.Has(Behaviour.Decoration) ? 0 :
        def.BlocksMovement ? 1 :
        def.Has(Behaviour.Platformer) || def.Has(Behaviour.Patrol) ? 3 : 2;

    public static void DrawSprite(DrawingContext ctx, string type, int frame, bool mirrored, double x, double y, double w, double h, double opacity = 1)
    {
        var bmp = SpriteCache.Get(type, frame, mirrored);
        using (ctx.PushRenderOptions(Pixelated))
        using (opacity < 1 ? ctx.PushOpacity(opacity) : default(DrawingContext.PushedState?))
            ctx.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), new Rect(x, y, w, h));
    }

    public static Color ParseColor(string hex, Color fallback)
    {
        try { return Color.Parse(hex); }
        catch { return fallback; }
    }

    /// <summary>The pixel scale used for a running game in a viewport of the given height.</summary>
    public static double GameScale(World w, double viewportHeight) =>
        Math.Max(0.5, viewportHeight / Math.Min(w.LevelHeight, 20 * w.Project.TileSize));

    public static void DrawWorld(DrawingContext ctx, World w, Rect bounds)
    {
        var scale = GameScale(w, bounds.Height);
        w.ViewWidth = bounds.Width / scale;
        w.ViewHeight = bounds.Height / scale;

        var sky = ParseColor(w.Level.Background, Colors.SkyBlue);
        ctx.FillRectangle(new SolidColorBrush(sky), bounds);

        // snap the camera to whole art pixels so tiles never shimmer
        var camX = Math.Round(w.CameraX * scale) / scale;
        var camY = Math.Round(w.CameraY * scale) / scale;

        // Each sprite's edges are rounded to whole screen pixels, so neighbouring tiles
        // meet exactly and no seams appear at fractional scales.
        double Sx(double x) => Math.Round(bounds.X + (x - camX) * scale);
        double Sy(double y) => Math.Round(bounds.Y + (y - camY) * scale);

        using (ctx.PushClip(bounds))
        {
            foreach (var e in w.Entities.OrderBy(e => Layer(e.Def)))
            {
                if (e.X + e.W < camX - 64 || e.X > camX + w.ViewWidth + 64) continue;
                var frame = World.FrameFor(e);
                var mirror = e.Facing < 0 && (e.Def.Has(Behaviour.Platformer) || e.Def.Has(Behaviour.Patrol));
                double y = e.Y, h = e.H;
                if (e.Squash > 0) { y += 4; h -= 4; }
                double left = Sx(e.X), top = Sy(y);
                DrawSprite(ctx, e.Type, frame, mirror, left, top, Sx(e.X + e.W) - left, Sy(y + h) - top);
            }
        }

        DrawHud(ctx, w, bounds);
    }

    static void DrawHud(DrawingContext ctx, World w, Rect bounds)
    {
        var x = bounds.X + 16;
        foreach (var kv in w.Vars)
        {
            var label = $"{kv.Key}  {kv.Value.ToString("0.##", CultureInfo.InvariantCulture)}";
            var text = Text(label, 16, Brushes.White, FontWeight.SemiBold);
            var pill = new Rect(x, bounds.Y + 14, text.Width + 24, text.Height + 10);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 17, 19, 23)), null, pill, 8, 8);
            ctx.DrawText(text, new Point(pill.X + 12, pill.Y + 5));
            x = pill.Right + 8;
        }

        if (!string.IsNullOrEmpty(w.Message))
        {
            var text = Text(w.Message!, 34, Brushes.White, FontWeight.Bold);
            var box = new Rect(bounds.Center.X - text.Width / 2 - 28, bounds.Center.Y - text.Height / 2 - 16, text.Width + 56, text.Height + 32);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 17, 19, 23)), null, box, 14, 14);
            ctx.DrawText(text, new Point(box.X + 28, box.Y + 16));
        }
    }

    public static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(UiTheme.UiFont, FontStyle.Normal, weight), size, brush);
}
