using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameEngine.Core;

/// <summary>A small RGBA image. Pixels are 0xAARRGGBB, row by row.</summary>
[JsonConverter(typeof(PixelImageJsonConverter))]
public sealed class PixelImage
{
    public const int MaxSize = 256;

    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public PixelImage(int w, int h)
    {
        Width = w;
        Height = h;
        Pixels = new uint[w * h];
    }

    public uint this[int x, int y]
    {
        get => x < 0 || y < 0 || x >= Width || y >= Height ? 0 : Pixels[y * Width + x];
        set { if (x >= 0 && y >= 0 && x < Width && y < Height) Pixels[y * Width + x] = value; }
    }

    public void Fill(int x, int y, int w, int h, uint c)
    {
        for (var j = y; j < y + h; j++)
            for (var i = x; i < x + w; i++)
                this[i, j] = c;
    }

    public void Ellipse(double cx, double cy, double rx, double ry, uint c)
    {
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                if (dx * dx + dy * dy <= 1) this[x, y] = c;
            }
    }

    /// <summary>Draws a one-pixel outline around every filled area.</summary>
    public void Outline(uint c)
    {
        var copy = (uint[])Pixels.Clone();
        bool Filled(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && copy[y * Width + x] != 0;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (Filled(x, y)) continue;
                if (Filled(x - 1, y) || Filled(x + 1, y) || Filled(x, y - 1) || Filled(x, y + 1))
                    this[x, y] = c;
            }
    }

    public PixelImage Clone()
    {
        var c = new PixelImage(Width, Height);
        Array.Copy(Pixels, c.Pixels, Pixels.Length);
        return c;
    }

    /// <summary>A new canvas size with the art kept at the bottom centre.</summary>
    public PixelImage Resized(int w, int h)
    {
        var r = new PixelImage(w, h);
        int ox = (w - Width) / 2, oy = h - Height;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                r[x + ox, y + oy] = this[x, y];
        return r;
    }

    /// <summary>A checkered box for objects that have no art yet.</summary>
    public static PixelImage Placeholder(int w, int h)
    {
        var img = new PixelImage(Math.Max(1, w), Math.Max(1, h));
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                img[x, y] = ((x / 4 + y / 4) % 2 == 0) ? 0xFFB06CFFu : 0xFF6C3FB0u;
        return img;
    }

    public PixelImage Mirrored()
    {
        var m = new PixelImage(Width, Height);
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                m[Width - 1 - x, y] = this[x, y];
        return m;
    }
}

/// <summary>
/// The built-in pixel art for every premade object. Everything is generated in code,
/// so the engine ships without any image files.
/// </summary>
public static class SpriteArt
{
    const uint Clear = 0;
    const uint Ink = 0xFF1B1D2A;

    static readonly Dictionary<string, PixelImage[]> Cache = new();

    /// <summary>Animation frames for an object type. Players also have named poses; see <see cref="PlayerPose"/>.</summary>
    public static PixelImage[] Frames(string type)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(type, out var frames))
                Cache[type] = frames = Build(type);
            return frames;
        }
    }

    public enum PlayerPose { Idle = 0, Run1 = 1, Run2 = 2, Jump = 3 }

    static PixelImage[] Build(string type) => type switch
    {
        "Player" => Player(),
        "Slime" => new[] { FromRows(SlimeA, SlimePalette), FromRows(SlimeB, SlimePalette) },
        "Coin" => Coin(),
        "Flag" => new[] { Flag(0), Flag(1) },
        "Spikes" => new[] { Spikes() },
        "Spring" => new[] { FromRows(SpringRows, SpringPalette) },
        "Grass" => new[] { Ground(grass: true) },
        "Dirt" => new[] { Ground(grass: false) },
        "Brick" => new[] { Brick() },
        "Stone" => new[] { Stone() },
        "MovingPlatform" => new[] { Platform() },
        "Ledge" => new[] { Ledge() },
        "Bush" => new[] { Bush() },
        "Cloud" => new[] { Cloud() },
        _ => new[] { Missing() },
    };

    static PixelImage FromRows(string[] rows, Dictionary<char, uint> palette)
    {
        var img = new PixelImage(rows[0].Length, rows.Length);
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                img[x, y] = palette.TryGetValue(rows[y][x], out var c) ? c : Clear;
        return img;
    }

    // Deterministic noise so tiles look the same every run.
    static double Noise(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0;
        }
    }

    // ---------------------------------------------------------------- characters

    static readonly Dictionary<char, uint> PlayerPalette = new()
    {
        ['k'] = Ink, ['b'] = 0xFF3E7BFA, ['B'] = 0xFF8FB8FF, ['w'] = 0xFFFFFFFF,
        ['e'] = 0xFF1B1D2A, ['d'] = 0xFF2A3550,
    };

    static readonly string[] PlayerTop =
    {
        "...kkkkkk...",
        "..kbbbbbbk..",
        ".kbBBbbbbbk.",
        ".kbBbbbbbbk.",
        ".kbbwwbwwbk.",
        ".kbbwebwebk.",
        ".kbbbbbbbbk.",
        ".kbbbkkbbbk.",
        "..kbbbbbbk..",
        "..kkbbbbkk..",
        ".kBBkbbkBBk.",
        ".kBbbbbbbBk.",
        "..kbbbbbbk..",
    };

    static PixelImage[] Player()
    {
        PixelImage Make(params string[] legs) => FromRows(PlayerTop.Concat(legs).ToArray(), PlayerPalette);
        return new[]
        {
            Make("..kbbkkbbk..", "..kddk.kddk.", "..kkkk.kkkk."),   // idle
            Make("..kbbkkbbk..", ".kddk...kddk", ".kkk.....kkk"),   // run 1
            Make("..kbbkkbbk..", "...kddddk...", "...kkkkkk..."),   // run 2
            Make("..kbbbbbbk..", "..kddkkddk..", "............"),   // jump
        };
    }

    static readonly Dictionary<char, uint> SlimePalette = new()
    {
        ['k'] = Ink, ['g'] = 0xFF4CC36A, ['G'] = 0xFFA8F0B0, ['w'] = 0xFFFFFFFF,
    };

    static readonly string[] SlimeA =
    {
        "................",
        "................",
        ".....kkkkkk.....",
        "...kkggggggkk...",
        "..kgGGggggggggk.",
        ".kgGgggggggggggk",
        ".kggwwggggwwgggk",
        ".kggwkggggwkgggk",
        ".kgggggggggggggk",
        ".kggggkkkkgggggk",
        ".kgggggggggggggk",
        "..kkkkkkkkkkkkk.",
    };

    static readonly string[] SlimeB =
    {
        "................",
        "................",
        "................",
        "....kkkkkkkk....",
        "..kkgGGgggggkk..",
        ".kgGgggggggggggk",
        ".kggwwggggwwgggk",
        ".kggwkggggwkgggk",
        ".kgggggggggggggk",
        ".kggggkkkkgggggk",
        "kggggggggggggggk",
        "kkkkkkkkkkkkkkkk",
    };

    // ---------------------------------------------------------------- items

    static readonly Dictionary<char, uint> CoinPalette = new()
    {
        ['k'] = 0xFF6B4A0E, ['y'] = 0xFFFFC93C, ['Y'] = 0xFFFFF1B0, ['o'] = 0xFFE09A12,
    };

    static PixelImage[] Coin()
    {
        var full = FromRows(new[]
        {
            "...kkkk...",
            "..kyyyyk..",
            ".kyYYyyyk.",
            ".kyYyyoyk.",
            "kyYyyyoyyk",
            "kyyyyyoyyk",
            "kyyyyyoyyk",
            "kyyyyyoyyk",
            ".kyyyyoyk.",
            ".kyyyyyyk.",
            "..kyyyyk..",
            "...kkkk...",
        }, CoinPalette);
        var narrow = FromRows(new[]
        {
            "...kkkk...",
            "...kyyk...",
            "..kyYyok..",
            "..kyYyok..",
            "..kyyyok..",
            "..kyyyok..",
            "..kyyyok..",
            "..kyyyok..",
            "..kyyyok..",
            "..kyyyok..",
            "...kyyk...",
            "...kkkk...",
        }, CoinPalette);
        var edge = FromRows(new[]
        {
            "....kk....",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "...kyok...",
            "....kk....",
        }, CoinPalette);
        return new[] { full, narrow, edge, narrow.Mirrored() };
    }

    static PixelImage Flag(int wave)
    {
        var img = new PixelImage(16, 32);
        // pole
        img.Fill(2, 2, 2, 28, 0xFFD9DDE8);
        img.Fill(3, 2, 1, 28, 0xFFA9AFC0);
        // ball on top
        img.Fill(1, 0, 4, 3, 0xFFFFC93C);
        img.Fill(2, 0, 1, 1, 0xFFFFF1B0);
        // waving cloth
        for (var x = 4; x < 15; x++)
        {
            var off = (int)Math.Round(Math.Sin((x + wave * 3) * 0.7) * 1.0);
            var len = 9 - (x - 4) / 3;
            for (var y = 0; y < len; y++)
                img[x, 3 + off + y + (x - 4) / 3] = y < 2 ? 0xFFFF7A70 : 0xFFF0524F;
        }
        // base
        img.Fill(0, 29, 6, 3, 0xFF8A90A6);
        img.Outline(Ink);
        return img;
    }

    static PixelImage Spikes()
    {
        var img = new PixelImage(16, 8);
        for (var s = 0; s < 2; s++)
            for (var y = 1; y < 8; y++)
            {
                var half = (y + 1) / 2;
                for (var x = 4 - half; x < 4 + half; x++)
                    img[s * 8 + x, y] = x < 4 ? 0xFFE4E7F0 : 0xFF9AA1B5;
            }
        img.Outline(Ink);
        return img;
    }

    static readonly Dictionary<char, uint> SpringPalette = new()
    {
        ['k'] = Ink, ['r'] = 0xFFF0524F, ['R'] = 0xFFFF9A92, ['s'] = 0xFFC9CED9, ['d'] = 0xFF5A6075,
    };

    static readonly string[] SpringRows =
    {
        "kkkkkkkkkkkkkkkk",
        "kRRRRRRRRRRRRRRk",
        "krrrrrrrrrrrrrrk",
        "kkkkkkkkkkkkkkkk",
        "....kssssssk....",
        ".....kssssk.....",
        "....kssssssk....",
        ".....kssssk.....",
        "kkkkkkkkkkkkkkkk",
        "kddddddddddddddk",
    };

    // ---------------------------------------------------------------- terrain

    static PixelImage Ground(bool grass)
    {
        var img = new PixelImage(16, 16);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
            {
                var n = Noise(x, y, grass ? 1 : 2);
                uint c = n > 0.86 ? 0xFF9A5B33 : n < 0.1 ? 0xFF6E3C22 : 0xFF84492A;
                img[x, y] = c;
            }
        if (grass)
        {
            for (var x = 0; x < 16; x++)
            {
                var depth = 4 + (Noise(x, 0, 7) > 0.5 ? 1 : 0) + (x % 5 == 0 ? 1 : 0);
                for (var y = 0; y < depth; y++)
                    img[x, y] = y == 0 ? 0xFF9BEA6A : y == depth - 1 ? 0xFF3E9E45 : 0xFF5CC85A;
            }
        }
        return img;
    }

    static PixelImage Brick()
    {
        var img = new PixelImage(16, 16);
        img.Fill(0, 0, 16, 16, 0xFFC4553A);
        for (var row = 0; row < 4; row++)
        {
            var y = row * 4;
            img.Fill(0, y + 3, 16, 1, 0xFF7A2E22);
            var offset = row % 2 == 0 ? 0 : 4;
            for (var x = offset; x < 16; x += 8) img.Fill(x, y, 1, 3, 0xFF7A2E22);
            img.Fill(0, y, 16, 1, 0xFFE07A5C);
        }
        return img;
    }

    static PixelImage Stone()
    {
        var img = new PixelImage(16, 16);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                img[x, y] = Noise(x, y, 3) > 0.8 ? 0xFF8E95A8 : 0xFF7A8196;
        img.Fill(0, 0, 16, 1, 0xFFB4BACB);
        img.Fill(0, 0, 1, 16, 0xFFA2A9BC);
        img.Fill(0, 15, 16, 1, 0xFF4E5366);
        img.Fill(15, 0, 1, 16, 0xFF5C6276);
        return img;
    }

    static PixelImage Platform()
    {
        var img = new PixelImage(48, 8);
        img.Fill(0, 0, 48, 8, Ink);
        img.Fill(1, 1, 46, 6, 0xFFF5A524);
        img.Fill(1, 1, 46, 1, 0xFFFFD27A);
        img.Fill(1, 6, 46, 1, 0xFFB86E0B);
        for (var x = 4; x < 48; x += 10)
        {
            img[x, 3] = Ink;
            img[x + 1, 3] = 0xFF6B4A0E;
        }
        return img;
    }

    static PixelImage Ledge()
    {
        var img = new PixelImage(32, 6);
        img.Fill(0, 0, 32, 6, Ink);
        img.Fill(1, 1, 30, 4, 0xFFB57B45);
        img.Fill(1, 1, 30, 1, 0xFFD9A066);
        for (var x = 8; x < 32; x += 8) img.Fill(x, 1, 1, 4, 0xFF7C4E27);
        for (var x = 3; x < 32; x += 8) img[x, 3] = 0xFF4A2F18;
        return img;
    }

    // ---------------------------------------------------------------- scenery

    static PixelImage Bush()
    {
        var img = new PixelImage(32, 16);
        img.Ellipse(9, 11, 8, 6, 0xFF3FAE52);
        img.Ellipse(17, 8, 9, 8, 0xFF3FAE52);
        img.Ellipse(25, 11, 6, 5, 0xFF3FAE52);
        img.Fill(2, 12, 28, 4, 0xFF3FAE52);
        img.Ellipse(15, 5, 4, 2.5, 0xFF7AD67F);
        img.Ellipse(7, 8, 3, 2, 0xFF7AD67F);
        // clip the outline room at the edges
        for (var x = 0; x < 32; x++) { img[x, 0] = 0; }
        img.Fill(0, 0, 1, 16, 0); img.Fill(31, 0, 1, 16, 0);
        img.Outline(0xFF21612D);
        return img;
    }

    static PixelImage Cloud()
    {
        var img = new PixelImage(32, 16);
        img.Ellipse(9, 10, 7, 5, 0xFFFFFFFF);
        img.Ellipse(17, 7, 8, 6, 0xFFFFFFFF);
        img.Ellipse(24, 10, 6, 4.5, 0xFFFFFFFF);
        img.Fill(4, 10, 24, 4, 0xFFFFFFFF);
        img.Fill(4, 13, 24, 1, 0xFFDDE9F5);
        img.Outline(0xFFB9D3EC);
        return img;
    }

    static PixelImage Missing()
    {
        var img = new PixelImage(16, 16);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                img[x, y] = ((x / 4 + y / 4) % 2 == 0) ? 0xFFFF00FF : 0xFF000000;
        return img;
    }
}

/// <summary>Stores an image compactly in project files: width, height and base64 pixels.</summary>
public sealed class PixelImageJsonConverter : JsonConverter<PixelImage>
{
    public override PixelImage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int w = 0, h = 0;
        string? data = null;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected an image object.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "w": w = reader.GetInt32(); break;
                case "h": h = reader.GetInt32(); break;
                case "rgba": data = reader.GetString(); break;
                default: reader.Skip(); break;
            }
        }
        w = Math.Clamp(w, 1, PixelImage.MaxSize);
        h = Math.Clamp(h, 1, PixelImage.MaxSize);
        var img = new PixelImage(w, h);
        if (data is not null)
        {
            var bytes = Convert.FromBase64String(data);
            for (var i = 0; i < img.Pixels.Length && i * 4 + 3 < bytes.Length; i++)
            {
                uint r = bytes[i * 4], g = bytes[i * 4 + 1], b = bytes[i * 4 + 2], a = bytes[i * 4 + 3];
                img.Pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }
        return img;
    }

    public override void Write(Utf8JsonWriter writer, PixelImage value, JsonSerializerOptions options)
    {
        var bytes = new byte[value.Pixels.Length * 4];
        for (var i = 0; i < value.Pixels.Length; i++)
        {
            var p = value.Pixels[i];
            bytes[i * 4] = (byte)(p >> 16);
            bytes[i * 4 + 1] = (byte)(p >> 8);
            bytes[i * 4 + 2] = (byte)p;
            bytes[i * 4 + 3] = (byte)(p >> 24);
        }
        writer.WriteStartObject();
        writer.WriteNumber("w", value.Width);
        writer.WriteNumber("h", value.Height);
        writer.WriteString("rgba", Convert.ToBase64String(bytes));
        writer.WriteEndObject();
    }
}
