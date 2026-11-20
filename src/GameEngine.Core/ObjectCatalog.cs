using System.Text.Json.Serialization;

namespace GameEngine.Core;

/// <summary>Built-in behaviours an object can have. Any combination is allowed.</summary>
[Flags]
public enum Behaviour
{
    None = 0,
    /// <summary>Player controls: run, jump, gravity.</summary>
    Platformer = 1,
    /// <summary>Blocks movement from every side.</summary>
    Solid = 2,
    /// <summary>Can be jumped through from below and stood on.</summary>
    JumpThru = 4,
    /// <summary>Travels back and forth and carries whatever stands on it.</summary>
    MovingPlatform = 8,
    /// <summary>Walks left and right, turning at walls (and optionally ledges).</summary>
    Patrol = 16,
    /// <summary>Launches the player upwards when landed on.</summary>
    Spring = 32,
    /// <summary>Background art only: no collisions at all.</summary>
    Decoration = 64,
    /// <summary>Flies in a straight line at a set angle.</summary>
    Bullet = 128,
    /// <summary>Bobs smoothly up and down (or side to side).</summary>
    Float = 256,
    /// <summary>Falls and lands on solid ground.</summary>
    Gravity = 512,
}

/// <summary>A number the user can tune, on an object type or per placed object.</summary>
public sealed record PropertyDef(string Name, string Label, double Default, double Min, double Max, double Step = 1, string Help = "");

/// <summary>Describes one behaviour for the editor: its name, what it does and its settings.</summary>
public sealed record BehaviourInfo(Behaviour Flag, string Name, string Description, PropertyDef[] Settings);

public static class BehaviourCatalog
{
    public static readonly IReadOnlyList<BehaviourInfo> All = new List<BehaviourInfo>
    {
        new(Behaviour.Platformer, "Platformer", "Controlled with the arrow keys or A/D, and Space/W/Up to jump.", new[]
        {
            new PropertyDef("Speed", "Run speed", 110, 0, 600, 5),
            new PropertyDef("JumpPower", "Jump power", 300, 0, 1200, 10),
            new PropertyDef("Gravity", "Gravity", 900, 0, 4000, 50),
            new PropertyDef("Jumps", "Number of jumps", 1, 1, 5, 1, "2 gives a double jump, 3 a triple jump."),
            new PropertyDef("Acceleration", "Acceleration", 12, 1, 60, 1, "How quickly it reaches full speed. Higher feels snappier."),
        }),
        new(Behaviour.Solid, "Solid", "Nothing can pass through it from any side.", Array.Empty<PropertyDef>()),
        new(Behaviour.JumpThru, "Jump-through", "Can be jumped up through from below, then stood on.", Array.Empty<PropertyDef>()),
        new(Behaviour.MovingPlatform, "Moving platform", "Slides back and forth and carries whatever stands on it.", new[]
        {
            new PropertyDef("DistanceX", "Distance X", 64, -4000, 4000, 8),
            new PropertyDef("DistanceY", "Distance Y", 0, -4000, 4000, 8),
            new PropertyDef("Speed", "Speed", 40, 1, 600, 5),
        }),
        new(Behaviour.Patrol, "Patrol", "Walks left and right on its own, turning at walls.", new[]
        {
            new PropertyDef("Speed", "Walk speed", 30, 0, 600, 5),
            new PropertyDef("Gravity", "Gravity", 900, 0, 4000, 50),
            new PropertyDef("TurnAtLedges", "Turn at ledges", 1, 0, 1, 1, "1 = turn around before walking off a ledge, 0 = walk off."),
        }),
        new(Behaviour.Spring, "Spring", "Bounces players that land on it.", new[]
        {
            new PropertyDef("Power", "Bounce power", 480, 0, 2000, 10),
        }),
        new(Behaviour.Bullet, "Bullet", "Flies in a straight line. Angle 0 is right, 90 is down, 180 is left, 270 is up.", new[]
        {
            new PropertyDef("Speed", "Speed", 150, 0, 2000, 10),
            new PropertyDef("Angle", "Angle", 0, 0, 359, 5),
            new PropertyDef("StopAtSolids", "Destroyed by walls", 1, 0, 1, 1, "1 = destroyed when it hits something solid."),
        }),
        new(Behaviour.Float, "Float", "Bobs smoothly back and forth. Good for coins and flying enemies.", new[]
        {
            new PropertyDef("FloatX", "Distance X", 0, -400, 400, 1),
            new PropertyDef("FloatY", "Distance Y", 4, -400, 400, 1),
            new PropertyDef("Period", "Seconds per bob", 1.5, 0.1, 20, 0.1),
        }),
        new(Behaviour.Gravity, "Gravity", "Falls and lands on solid ground, like a crate.", new[]
        {
            new PropertyDef("Gravity", "Gravity", 900, 0, 4000, 50),
        }),
        new(Behaviour.Decoration, "Decoration", "Background art. Never collides and is drawn behind everything.", Array.Empty<PropertyDef>()),
    };

    public static BehaviourInfo Get(Behaviour b) => All.First(i => i.Flag == b);

    /// <summary>The settings for a set of behaviours. Shared names (like Speed) appear once.</summary>
    public static IEnumerable<PropertyDef> SettingsFor(Behaviour behaviours)
    {
        var seen = new HashSet<string>();
        foreach (var info in All)
            if ((behaviours & info.Flag) != 0)
                foreach (var p in info.Settings)
                    if (seen.Add(p.Name)) yield return p;
    }
}

/// <summary>One named animation: a list of frames played at a speed.</summary>
public sealed class SpriteAnimation
{
    public string Name { get; set; } = "Default";
    public double Fps { get; set; } = 8;
    public bool Loop { get; set; } = true;
    public List<PixelImage> Frames { get; set; } = new();

    public SpriteAnimation Clone() => new()
    {
        Name = Name, Fps = Fps, Loop = Loop,
        Frames = Frames.Select(f => f.Clone()).ToList(),
    };
}

/// <summary>
/// A kind of object: its sprite, size, behaviours, settings and variables. Every project holds
/// its own copies (the premade ones included), so everything can be edited.
/// </summary>
public sealed class ObjectType
{
    /// <summary>Stable name used by levels and events. Never changes after creation.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "My objects";
    public string Description { get; set; } = "";

    /// <summary>Collision box size in pixels; also the sprite's canvas size.</summary>
    public int Width { get; set; } = 16;
    public int Height { get; set; } = 16;

    public Behaviour Behaviours { get; set; }

    /// <summary>Default values for this type's behaviour settings (placed objects can override them).</summary>
    public Dictionary<string, double> Settings { get; set; } = new();

    /// <summary>Numbers each placed object keeps for itself, e.g. Health.</summary>
    public Dictionary<string, double> Variables { get; set; } = new();

    public List<SpriteAnimation> Animations { get; set; } = new();

    public bool Has(Behaviour b) => (Behaviours & b) != 0;

    [JsonIgnore] public bool BlocksMovement => Has(Behaviour.Solid) || Has(Behaviour.JumpThru);

    /// <summary>The tunable settings for this type's behaviours, with this type's defaults.</summary>
    [JsonIgnore]
    public PropertyDef[] Properties => BehaviourCatalog.SettingsFor(Behaviours)
        .Select(p => Settings.TryGetValue(p.Name, out var v) ? p with { Default = v } : p)
        .ToArray();

    public double Setting(string name)
    {
        if (Settings.TryGetValue(name, out var v)) return v;
        return BehaviourCatalog.SettingsFor(Behaviours).FirstOrDefault(p => p.Name == name)?.Default ?? 0;
    }

    public SpriteAnimation? Animation(string name) =>
        Animations.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) && a.Frames.Count > 0);

    /// <summary>The first frame of the first animation, used for icons and the level editor.</summary>
    [JsonIgnore]
    public PixelImage Icon => Animations.FirstOrDefault(a => a.Frames.Count > 0)?.Frames[0] ?? PixelImage.Placeholder(Width, Height);

    public ObjectType Clone() => new()
    {
        Id = Id, Name = Name, Category = Category, Description = Description,
        Width = Width, Height = Height, Behaviours = Behaviours,
        Settings = new Dictionary<string, double>(Settings),
        Variables = new Dictionary<string, double>(Variables),
        Animations = Animations.Select(a => a.Clone()).ToList(),
    };

    /// <summary>Changes the canvas size of every frame, keeping art anchored to the bottom centre.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Clamp(width, 1, PixelImage.MaxSize);
        height = Math.Clamp(height, 1, PixelImage.MaxSize);
        foreach (var a in Animations)
            for (var i = 0; i < a.Frames.Count; i++)
                a.Frames[i] = a.Frames[i].Resized(width, height);
        Width = width;
        Height = height;
    }
}

/// <summary>The premade objects every new project starts with.</summary>
public static class BuiltInObjects
{
    public static readonly string[] Categories = { "Characters", "Terrain", "Platforms", "Items", "Hazards", "Scenery" };

    public static List<ObjectType> Create() => new()
    {
        Make("Player", "Player", "Characters", "The hero. Arrow keys or A/D to run, Space/W/Up to jump.", 12, 16, Behaviour.Platformer,
            Anim("Idle", 1, Frame("Player", 0)),
            Anim("Run", 10, Frame("Player", 1), Frame("Player", 2)),
            Anim("Jump", 1, Frame("Player", 3))),
        Make("Slime", "Slime", "Characters", "Patrols back and forth. Stomp it from above.", 16, 12, Behaviour.Patrol,
            Anim("Default", 4, SpriteArt.Frames("Slime"))),
        Make("Grass", "Grass", "Terrain", "Solid ground with a grassy top.", 16, 16, Behaviour.Solid, Anim("Default", 0, SpriteArt.Frames("Grass"))),
        Make("Dirt", "Dirt", "Terrain", "Solid ground.", 16, 16, Behaviour.Solid, Anim("Default", 0, SpriteArt.Frames("Dirt"))),
        Make("Brick", "Brick", "Terrain", "Solid brick wall.", 16, 16, Behaviour.Solid, Anim("Default", 0, SpriteArt.Frames("Brick"))),
        Make("Stone", "Stone", "Terrain", "Solid grey stone.", 16, 16, Behaviour.Solid, Anim("Default", 0, SpriteArt.Frames("Stone"))),
        Make("MovingPlatform", "Platform", "Platforms", "Slides back and forth and carries the player.", 48, 8,
            Behaviour.Solid | Behaviour.MovingPlatform, Anim("Default", 0, SpriteArt.Frames("MovingPlatform"))),
        Make("Ledge", "Ledge", "Platforms", "Jump up through it from below, then stand on it.", 32, 6, Behaviour.JumpThru,
            Anim("Default", 0, SpriteArt.Frames("Ledge"))),
        Make("Spring", "Spring", "Platforms", "Bounces the player high into the air.", 16, 10, Behaviour.Spring,
            Anim("Default", 0, SpriteArt.Frames("Spring"))),
        Make("Coin", "Coin", "Items", "Collect it to add to Score (see the event sheet).", 10, 12, Behaviour.None,
            Anim("Default", 8, SpriteArt.Frames("Coin"))),
        Make("Flag", "Goal", "Items", "Touch it to finish the level.", 16, 32, Behaviour.None, Anim("Default", 4, SpriteArt.Frames("Flag"))),
        Make("Spikes", "Spikes", "Hazards", "Touching these restarts the level.", 16, 8, Behaviour.None, Anim("Default", 0, SpriteArt.Frames("Spikes"))),
        Make("Bush", "Bush", "Scenery", "Decoration only.", 32, 16, Behaviour.Decoration, Anim("Default", 0, SpriteArt.Frames("Bush"))),
        Make("Cloud", "Cloud", "Scenery", "Decoration only.", 32, 16, Behaviour.Decoration, Anim("Default", 0, SpriteArt.Frames("Cloud"))),
    };

    public static ObjectType? Find(string id) => Create().FirstOrDefault(t => t.Id == id);

    static PixelImage Frame(string art, int index) => SpriteArt.Frames(art)[index];

    static SpriteAnimation Anim(string name, double fps, params PixelImage[] frames) => new()
    {
        Name = name, Fps = fps, Frames = frames.Select(f => f.Clone()).ToList(),
    };

    static ObjectType Make(string id, string name, string category, string description, int w, int h, Behaviour b, params SpriteAnimation[] anims) => new()
    {
        Id = id, Name = name, Category = category, Description = description,
        Width = w, Height = h, Behaviours = b, Animations = anims.ToList(),
    };
}
