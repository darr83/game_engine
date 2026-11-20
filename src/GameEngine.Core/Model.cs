using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameEngine.Core;

/// <summary>A whole game: its levels, its event sheet and its global variables.</summary>
public sealed class Project
{
    public string Name { get; set; } = "My Platformer";
    public int TileSize { get; set; } = 16;
    public List<Level> Levels { get; set; } = new();
    public List<GameEvent> Events { get; set; } = new();

    /// <summary>Every kind of object in this game, premade and custom, with their sprites.</summary>
    public List<ObjectType> Objects { get; set; } = new();

    /// <summary>Sound effects and music.</summary>
    public List<SoundAsset> Sounds { get; set; } = new();

    /// <summary>Global variables and their starting values (e.g. Score = 0).</summary>
    public Dictionary<string, double> Variables { get; set; } = new() { ["Score"] = 0 };

    public Project Clone() => ProjectIO.FromJson(ProjectIO.ToJson(this));

    public ObjectType? FindType(string id) => Objects.FirstOrDefault(o => o.Id == id);

    public string TypeName(string id) => FindType(id)?.Name ?? id;

    public SoundAsset? FindSound(string name) => Sounds.FirstOrDefault(s => s.Name == name);

    public string NewSoundName(string baseName)
    {
        var name = baseName;
        for (var n = 2; FindSound(name) is not null; n++) name = $"{baseName} {n}";
        return name;
    }

    /// <summary>A new unique id based on a display name, e.g. "Bat", "Bat2".</summary>
    public string NewTypeId(string name)
    {
        var baseId = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (baseId.Length == 0 || char.IsDigit(baseId[0])) baseId = "Object" + baseId;
        var id = baseId;
        for (var n = 2; FindType(id) is not null; n++) id = baseId + n;
        return id;
    }

    /// <summary>Adds the premade objects to projects saved before object types were editable.</summary>
    public void EnsureObjects()
    {
        var builtIns = BuiltInObjects.Create();
        if (Objects.Count == 0) { Objects.AddRange(builtIns); return; }
        foreach (var id in Levels.SelectMany(l => l.Instances).Select(i => i.Type).Distinct())
            if (FindType(id) is null && builtIns.FirstOrDefault(b => b.Id == id) is { } b)
                Objects.Add(b);
    }
}

public sealed class Level
{
    public string Name { get; set; } = "Level 1";

    /// <summary>Size in tiles.</summary>
    public int Width { get; set; } = 60;
    public int Height { get; set; } = 17;

    /// <summary>Sky colour as #RRGGBB.</summary>
    public string Background { get; set; } = "#8ED6FF";

    /// <summary>Name of the sound to loop as this level's music; empty for silence.</summary>
    public string Music { get; set; } = "";

    /// <summary>0 to 1.</summary>
    public double MusicVolume { get; set; } = 0.6;

    public List<Instance> Instances { get; set; } = new();

    public int NextId() => Instances.Count == 0 ? 1 : Instances.Max(i => i.Id) + 1;
}

/// <summary>One placed object in a level.</summary>
public sealed class Instance
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Overrides for the object's tunable properties (speed, distance...).</summary>
    public Dictionary<string, double> Props { get; set; } = new();

    public double Get(ObjectType type, string prop) => Props.TryGetValue(prop, out var v) ? v : type.Setting(prop);
}

/// <summary>One row of the event sheet: when all conditions are true, run the actions.</summary>
public sealed class GameEvent
{
    public bool Enabled { get; set; } = true;
    public string Comment { get; set; } = "";
    public List<EventItem> Conditions { get; set; } = new();
    public List<EventItem> Actions { get; set; } = new();
}

/// <summary>A condition or action: a kind (see <see cref="EventCatalog"/>) plus its arguments.</summary>
public sealed class EventItem
{
    public string Kind { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();

    public EventItem() { }
    public EventItem(string kind, params (string key, string value)[] args)
    {
        Kind = kind;
        foreach (var (k, v) in args) Args[k] = v;
    }

    public string Arg(string key, string fallback = "") => Args.TryGetValue(key, out var v) ? v : fallback;
    public double Num(string key, double fallback = 0) =>
        double.TryParse(Arg(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

public static class ProjectIO
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public const string Extension = ".geproj";

    public static string ToJson(Project p) => JsonSerializer.Serialize(p, Options);
    public static Project FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<Project>(json, Options) ?? new Project();
        p.EnsureObjects();
        return p;
    }

    public static void Save(Project p, string path) => File.WriteAllText(path, ToJson(p));
    public static Project Load(string path) => FromJson(File.ReadAllText(path));
}
