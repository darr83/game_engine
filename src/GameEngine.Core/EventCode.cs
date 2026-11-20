using System.Text;

namespace GameEngine.Core;

/// <summary>
/// Shows an event sheet as the equivalent C#. Each event becomes one method:
/// conditions become loops and ifs, actions become statements.
/// </summary>
public static class EventCode
{
    public static string ToCSharp(Project project)
    {
        var sb = new StringBuilder();
        var cls = Identifier(project.Name);
        sb.AppendLine($"// Event sheet for \"{project.Name}\", shown as C#.");
        sb.AppendLine("// Each event runs once per tick (60 times a second), top to bottom.");
        sb.AppendLine();
        sb.AppendLine($"public class {cls} : EventSheet");
        sb.AppendLine("{");

        if (project.Variables.Count > 0)
        {
            foreach (var kv in project.Variables)
                sb.AppendLine($"    public double {Identifier(kv.Key)} = {Num(kv.Value)};");
            sb.AppendLine();
        }

        for (var i = 0; i < project.Events.Count; i++)
        {
            var ev = project.Events[i];
            if (!string.IsNullOrWhiteSpace(ev.Comment)) sb.AppendLine($"    // {ev.Comment}");
            if (!ev.Enabled) sb.AppendLine("    // (disabled)");
            sb.AppendLine($"    void Event{i + 1}()");
            sb.AppendLine("    {");
            Body(sb, ev, project);
            sb.AppendLine("    }");
            if (i < project.Events.Count - 1) sb.AppendLine();
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void Body(StringBuilder sb, GameEvent ev, Project project)
    {
        var depth = 2;
        string Pad() => new(' ', depth * 4);
        string? a = null, b = null;

        foreach (var c in ev.Conditions)
        {
            string ta = c.Arg("A"), tb = c.Arg("B");
            string Var(string type) => Unique(char.ToLowerInvariant(type.FirstOrDefault('x')) + type[1..], a, b);
            switch (c.Kind)
            {
                case "Collides" or "Stomps":
                {
                    var va = a ?? Var(ta);
                    var vb = Var(tb);
                    var fn = c.Kind == "Collides" ? "Touching" : "LandsOn";
                    if (a is null)
                        sb.AppendLine($"{Pad()}foreach (var ({va}, {vb}) in {fn}<{ta}, {tb}>())");
                    else
                        sb.AppendLine($"{Pad()}foreach (var {vb} in {va}.{fn}<{tb}>())");
                    sb.AppendLine($"{Pad()}{{");
                    depth++;
                    a = va;
                    b = vb;
                    break;
                }
                case "FallsOut" or "IsOnGround":
                {
                    var test = c.Kind == "FallsOut" ? "IsBelowLevel" : "IsOnGround";
                    if (a is null)
                    {
                        a = Var(ta);
                        sb.AppendLine($"{Pad()}foreach (var {a} in All<{ta}>().Where(o => o.{test}))");
                    }
                    else
                    {
                        sb.AppendLine($"{Pad()}if ({a}.{test})");
                    }
                    sb.AppendLine($"{Pad()}{{");
                    depth++;
                    break;
                }
                default:
                    sb.AppendLine($"{Pad()}if ({Expression(c)})");
                    sb.AppendLine($"{Pad()}{{");
                    depth++;
                    break;
            }
        }

        if (ev.Actions.Count == 0) sb.AppendLine($"{Pad()}// no actions yet");
        foreach (var act in ev.Actions) sb.AppendLine($"{Pad()}{Statement(act, a, b)}");

        while (depth > 2)
        {
            depth--;
            sb.AppendLine($"{Pad()}}}");
        }
    }

    static string Unique(string name, string? a, string? b) => name == a || name == b ? name + "2" : name;

    static string Expression(EventItem c) => c.Kind switch
    {
        "OnStart" => "LevelJustStarted",
        "EveryTick" => "true",
        "EverySeconds" => $"Every({Num(c.Num("Seconds", 1))}s)",
        "CountIs" => $"Count<{c.Arg("Type")}>() {CsOp(c.Arg("Op", "="))} {Num(c.Num("Value"))}",
        "KeyPressed" => $"Keyboard.Pressed(Key.{c.Arg("Key")})",
        "KeyDown" => $"Keyboard.IsDown(Key.{c.Arg("Key")})",
        "Compare" => $"{Identifier(c.Arg("Var"))} {CsOp(c.Arg("Op", "="))} {Num(c.Num("Value"))}",
        _ => $"/* {c.Kind} */ false",
    };

    static string Statement(EventItem act, string? a, string? b)
    {
        string Target() => (act.Arg("Target", "A") == "B" ? b : a) ?? "/* no object picked */ null";
        return act.Kind switch
        {
            "Destroy" => $"Destroy({Target()});",
            "Spawn" => $"Create<{act.Arg("Type")}>(at: {Target()});",
            "Bounce" => $"{Target()}.VelocityY = -{Num(act.Num("Power", 250))};",
            "AddVar" => $"{Identifier(act.Arg("Var"))} += {Num(act.Num("Value"))};",
            "SetVar" => $"{Identifier(act.Arg("Var"))} = {Num(act.Num("Value"))};",
            "ShowMessage" => $"ShowMessage($\"{act.Arg("Text").Replace("\"", "\\\"")}\", seconds: {Num(act.Num("Seconds", 2))});",
            "RestartLevel" => "RestartLevel();",
            "NextLevel" => "GoToNextLevel();",
            _ => $"// unknown action {act.Kind}",
        };
    }

    static string CsOp(string op) => op == "=" ? "==" : op;

    static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    static string Identifier(string s)
    {
        var sb = new StringBuilder();
        var upper = true;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                sb.Append(upper ? char.ToUpperInvariant(ch) : ch);
                upper = false;
            }
            else upper = true;
        }
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
