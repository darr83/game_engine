namespace GameEngine.Core;

/// <summary>Built-in behaviours an object can have.</summary>
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
    /// <summary>Walks left and right, turning at walls and ledges.</summary>
    Patrol = 16,
    /// <summary>Launches the player upwards when landed on.</summary>
    Spring = 32,
    /// <summary>Background art only: no collisions at all.</summary>
    Decoration = 64,
}

/// <summary>A number the user can tune per placed object in the inspector.</summary>
public sealed record PropertyDef(string Name, string Label, double Default, double Min, double Max, double Step = 1);

/// <summary>A premade object that can be dragged into a level.</summary>
public sealed record ObjectDef(
    string Id,
    string Name,
    string Category,
    string Description,
    int Width,
    int Height,
    Behaviour Behaviours,
    PropertyDef[] Properties,
    double AnimFps = 0)
{
    public bool Has(Behaviour b) => (Behaviours & b) != 0;
    public bool BlocksMovement => Has(Behaviour.Solid) || Has(Behaviour.JumpThru);
}

public static class ObjectCatalog
{
    public static readonly string[] Categories = { "Characters", "Terrain", "Platforms", "Items", "Hazards", "Scenery" };

    public static readonly IReadOnlyList<ObjectDef> All = new List<ObjectDef>
    {
        new("Player", "Player", "Characters", "The hero. Arrow keys or A/D to run, Space/W/Up to jump.",
            12, 16, Behaviour.Platformer, new[]
            {
                new PropertyDef("Speed", "Run speed", 110, 20, 400, 5),
                new PropertyDef("JumpPower", "Jump power", 300, 50, 800, 10),
                new PropertyDef("Gravity", "Gravity", 900, 100, 3000, 50),
            }, AnimFps: 10),

        new("Slime", "Slime", "Characters", "Patrols back and forth. Stomp it from above.",
            16, 12, Behaviour.Patrol, new[]
            {
                new PropertyDef("Speed", "Walk speed", 30, 0, 300, 5),
            }, AnimFps: 4),

        new("Grass", "Grass", "Terrain", "Solid ground with a grassy top.",
            16, 16, Behaviour.Solid, Array.Empty<PropertyDef>()),
        new("Dirt", "Dirt", "Terrain", "Solid ground.",
            16, 16, Behaviour.Solid, Array.Empty<PropertyDef>()),
        new("Brick", "Brick", "Terrain", "Solid brick wall.",
            16, 16, Behaviour.Solid, Array.Empty<PropertyDef>()),
        new("Stone", "Stone", "Terrain", "Solid grey stone.",
            16, 16, Behaviour.Solid, Array.Empty<PropertyDef>()),

        new("MovingPlatform", "Platform", "Platforms", "Slides back and forth and carries the player.",
            48, 8, Behaviour.Solid | Behaviour.MovingPlatform, new[]
            {
                new PropertyDef("DistanceX", "Distance X", 64, -2000, 2000, 8),
                new PropertyDef("DistanceY", "Distance Y", 0, -2000, 2000, 8),
                new PropertyDef("Speed", "Speed", 40, 1, 400, 5),
            }),
        new("Ledge", "Ledge", "Platforms", "Jump up through it from below, then stand on it.",
            32, 6, Behaviour.JumpThru, Array.Empty<PropertyDef>()),
        new("Spring", "Spring", "Platforms", "Bounces the player high into the air.",
            16, 10, Behaviour.Spring, new[]
            {
                new PropertyDef("Power", "Bounce power", 480, 100, 1500, 10),
            }),

        new("Coin", "Coin", "Items", "Collect it to add to Score (see the event sheet).",
            10, 12, Behaviour.None, Array.Empty<PropertyDef>(), AnimFps: 8),
        new("Flag", "Goal", "Items", "Touch it to finish the level.",
            16, 32, Behaviour.None, Array.Empty<PropertyDef>(), AnimFps: 4),

        new("Spikes", "Spikes", "Hazards", "Touching these restarts the level.",
            16, 8, Behaviour.None, Array.Empty<PropertyDef>()),

        new("Bush", "Bush", "Scenery", "Decoration only.",
            32, 16, Behaviour.Decoration, Array.Empty<PropertyDef>()),
        new("Cloud", "Cloud", "Scenery", "Decoration only.",
            32, 16, Behaviour.Decoration, Array.Empty<PropertyDef>()),
    };

    static readonly Dictionary<string, ObjectDef> ById = All.ToDictionary(d => d.Id);

    public static ObjectDef? Find(string id) => ById.TryGetValue(id, out var d) ? d : null;
    public static ObjectDef Get(string id) => ById[id];
    public static IEnumerable<string> TypeNames => All.Select(d => d.Id);
}
