using GameEngine.Core;

namespace GameEngine.Editor;

/// <summary>
/// Undo history as whole-project snapshots. Imported audio is shared between snapshots
/// rather than copied, so long music files don't fill memory.
/// </summary>
public sealed class UndoStack
{
    const int Limit = 200;

    sealed record Snapshot(string Json, byte[]?[] Audio, int Level);

    readonly LinkedList<Snapshot> _undo = new();
    readonly Stack<Snapshot> _redo = new();

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    static Snapshot Capture(Project p, int level)
    {
        var audio = p.Sounds.Select(s => s.Data).ToArray();
        foreach (var s in p.Sounds) s.Data = null;
        try { return new Snapshot(ProjectIO.ToJson(p), audio, level); }
        finally { for (var i = 0; i < audio.Length; i++) p.Sounds[i].Data = audio[i]; }
    }

    static (Project, int) Rebuild(Snapshot snap)
    {
        var p = ProjectIO.FromJson(snap.Json);
        for (var i = 0; i < p.Sounds.Count && i < snap.Audio.Length; i++) p.Sounds[i].Data = snap.Audio[i];
        return (p, snap.Level);
    }

    public void Push(Project current, int level)
    {
        _undo.AddLast(Capture(current, level));
        if (_undo.Count > Limit) _undo.RemoveFirst();
        _redo.Clear();
    }

    public (Project, int)? Undo(Project current, int level)
    {
        if (_undo.Last is not { } last) return null;
        _undo.RemoveLast();
        _redo.Push(Capture(current, level));
        return Rebuild(last.Value);
    }

    public (Project, int)? Redo(Project current, int level)
    {
        if (_redo.Count == 0) return null;
        var next = _redo.Pop();
        _undo.AddLast(Capture(current, level));
        return Rebuild(next);
    }
}

/// <summary>
/// Writes a stand-alone game: the player program, renamed after the game, plus game.json.
/// </summary>
public static class Exporter
{
    public sealed record Result(bool Ok, string Message);

    const string PlayerName = "GameEngine.Player";

    public static Result Export(Project project, string folder)
    {
        var safeName = string.Concat(project.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (safeName.Length == 0) safeName = "Game";
        var target = Path.Combine(folder, safeName);
        Directory.CreateDirectory(target);
        ProjectIO.Save(project, Path.Combine(target, "game.json"));

        var player = FindPlayer();
        if (player is null)
            return new(false, $"Saved game.json to {target}, but the player program was not found next to the editor. Build GameEngine.Player first (see README).");

        foreach (var file in Directory.GetFiles(player, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(player, file);
            if (rel.EndsWith(".pdb") || rel == "game.json") continue;
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        // rename the launcher after the game; it still finds GameEngine.Player.dll
        foreach (var exe in new[] { PlayerName + ".exe", PlayerName })
        {
            var from = Path.Combine(target, exe);
            if (!File.Exists(from)) continue;
            var to = Path.Combine(target, safeName + (exe.EndsWith(".exe") ? ".exe" : ""));
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
        return new(true, $"Exported \"{project.Name}\" to {target}.");
    }

    /// <summary>
    /// Release builds ship the player in a "player" folder next to the editor. During
    /// development it is found in the player project's build output.
    /// </summary>
    static string? FindPlayer()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "player");
        if (File.Exists(Path.Combine(bundled, PlayerName + ".dll"))) return bundled;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src", PlayerName, "bin");
            if (!Directory.Exists(src)) continue;
            return Directory.GetFiles(src, PlayerName + ".dll", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d!))
                .FirstOrDefault();
        }
        return null;
    }
}
