namespace GameEngine.Core;

public enum ParamType
{
    /// <summary>One of the object types from <see cref="ObjectCatalog"/>.</summary>
    ObjectType,
    /// <summary>Which object from the conditions to act on: A (first) or B (second).</summary>
    Target,
    /// <summary>A keyboard key.</summary>
    Key,
    Number,
    /// <summary>A global variable name.</summary>
    Variable,
    /// <summary>A comparison operator: =, !=, &lt;, &lt;=, &gt;, &gt;=.</summary>
    Compare,
    Text,
}

public sealed record ParamDef(string Key, string Label, ParamType Type, string Default);

/// <summary>
/// Describes a condition or action kind. <see cref="Template"/> is the sentence shown in the
/// event sheet, with {Key} placeholders for its parameters.
/// </summary>
public sealed record EventKind(string Kind, string Name, string Category, string Template, ParamDef[] Params)
{
    public string Describe(EventItem item)
    {
        var text = Template;
        foreach (var p in Params)
        {
            var value = item.Arg(p.Key, p.Default);
            if (p.Type == ParamType.Target) value = value == "B" ? "the other object" : "the first object";
            if (p.Type == ParamType.Text) value = $"\"{value}\"";
            text = text.Replace("{" + p.Key + "}", value);
        }
        return text;
    }

    public EventItem CreateDefault()
    {
        var item = new EventItem { Kind = Kind };
        foreach (var p in Params) item.Args[p.Key] = p.Default;
        return item;
    }
}

public static class EventCatalog
{
    public static readonly string[] Keys =
        { "Space", "Left", "Right", "Up", "Down", "W", "A", "S", "D", "Z", "X", "C", "Enter", "Escape", "Shift" };

    public static readonly string[] Comparisons = { "=", "!=", "<", "<=", ">", ">=" };

    public static readonly IReadOnlyList<EventKind> Conditions = new List<EventKind>
    {
        new("OnStart", "Level starts", "System", "Level starts", Array.Empty<ParamDef>()),
        new("EveryTick", "Every tick", "System", "Every tick", Array.Empty<ParamDef>()),
        new("EverySeconds", "Every X seconds", "System", "Every {Seconds} seconds",
            new[] { new ParamDef("Seconds", "Seconds", ParamType.Number, "1") }),

        new("Collides", "Objects touch", "Objects", "{A} touches {B}",
            new[] { new ParamDef("A", "Object", ParamType.ObjectType, "Player"), new ParamDef("B", "Touches", ParamType.ObjectType, "Coin") }),
        new("Stomps", "Object lands on top of another", "Objects", "{A} stomps on {B}",
            new[] { new ParamDef("A", "Object", ParamType.ObjectType, "Player"), new ParamDef("B", "Lands on", ParamType.ObjectType, "Slime") }),
        new("FallsOut", "Object falls out of the level", "Objects", "{A} falls out of the level",
            new[] { new ParamDef("A", "Object", ParamType.ObjectType, "Player") }),
        new("IsOnGround", "Object is on the ground", "Objects", "{A} is on the ground",
            new[] { new ParamDef("A", "Object", ParamType.ObjectType, "Player") }),
        new("CountIs", "Number of objects", "Objects", "Number of {Type} {Op} {Value}",
            new[] { new ParamDef("Type", "Object", ParamType.ObjectType, "Coin"), new ParamDef("Op", "Is", ParamType.Compare, "="), new ParamDef("Value", "Value", ParamType.Number, "0") }),

        new("KeyPressed", "Key is pressed", "Keyboard", "{Key} key is pressed",
            new[] { new ParamDef("Key", "Key", ParamType.Key, "Space") }),
        new("KeyDown", "Key is held down", "Keyboard", "{Key} key is held down",
            new[] { new ParamDef("Key", "Key", ParamType.Key, "Space") }),

        new("Compare", "Compare variable", "Variables", "{Var} {Op} {Value}",
            new[] { new ParamDef("Var", "Variable", ParamType.Variable, "Score"), new ParamDef("Op", "Is", ParamType.Compare, ">="), new ParamDef("Value", "Value", ParamType.Number, "10") }),
    };

    public static readonly IReadOnlyList<EventKind> Actions = new List<EventKind>
    {
        new("Destroy", "Destroy object", "Objects", "Destroy {Target}",
            new[] { new ParamDef("Target", "Object", ParamType.Target, "B") }),
        new("Spawn", "Create object", "Objects", "Create a {Type} at {Target}",
            new[] { new ParamDef("Type", "New object", ParamType.ObjectType, "Coin"), new ParamDef("Target", "Position of", ParamType.Target, "A") }),
        new("Bounce", "Bounce upwards", "Objects", "Bounce {Target} up with power {Power}",
            new[] { new ParamDef("Target", "Object", ParamType.Target, "A"), new ParamDef("Power", "Power", ParamType.Number, "250") }),

        new("AddVar", "Add to variable", "Variables", "Add {Value} to {Var}",
            new[] { new ParamDef("Var", "Variable", ParamType.Variable, "Score"), new ParamDef("Value", "Amount", ParamType.Number, "1") }),
        new("SetVar", "Set variable", "Variables", "Set {Var} to {Value}",
            new[] { new ParamDef("Var", "Variable", ParamType.Variable, "Score"), new ParamDef("Value", "Value", ParamType.Number, "0") }),

        new("ShowMessage", "Show message", "Game", "Show message {Text}",
            new[] { new ParamDef("Text", "Message", ParamType.Text, "Well done!"), new ParamDef("Seconds", "Seconds", ParamType.Number, "2") }),
        new("RestartLevel", "Restart level", "Game", "Restart the level", Array.Empty<ParamDef>()),
        new("NextLevel", "Go to next level", "Game", "Go to the next level", Array.Empty<ParamDef>()),
    };

    public static EventKind? FindCondition(string kind) => Conditions.FirstOrDefault(c => c.Kind == kind);
    public static EventKind? FindAction(string kind) => Actions.FirstOrDefault(c => c.Kind == kind);

    public static string Describe(EventItem item, bool isCondition)
    {
        var def = isCondition ? FindCondition(item.Kind) : FindAction(item.Kind);
        return def?.Describe(item) ?? $"Unknown: {item.Kind}";
    }
}
