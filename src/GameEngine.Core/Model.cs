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

    /// <summary>Global variables and their starting values (e.g. Score = 0).</summary>
    public Dictionary<string, double> Variables { get; set; } = new() { ["Score"] = 0 };

    public Project Clone() => ProjectIO.FromJson(ProjectIO.ToJson(this));
}

public sealed class Level
{
    public string Name { get; set; } = "Level 1";

    /// <summary>Size in tiles.</summary>
    public int Width { get; set; } = 60;
    public int Height { get; set; } = 17;

    /// <summary>Sky colour as #RRGGBB.</summary>
    public string Background { get; set; } = "#8ED6FF";

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

    public double Get(ObjectDef def, string prop)
    {
        if (Props.TryGetValue(prop, out var v)) return v;
        var p = def.Properties.FirstOrDefault(x => x.Name == prop);
        return p?.Default ?? 0;
    }
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
    public static Project FromJson(string json) => JsonSerializer.Deserialize<Project>(json, Options) ?? new Project();

    public static void Save(Project p, string path) => File.WriteAllText(path, ToJson(p));
    public static Project Load(string path) => FromJson(File.ReadAllText(path));
}
