namespace GameEngine.Core;

/// <summary>The starter project every new game begins from.</summary>
public static class SampleProject
{
    public static Project Create()
    {
        var p = new Project { Name = "My Platformer", Objects = BuiltInObjects.Create() };
        p.Objects.Add(Gem());
        p.Levels.Add(Level1());
        p.Levels.Add(Level2());
        p.Events.AddRange(DefaultEvents());
        p.Events.Insert(1, new GameEvent
        {
            Comment = "Gems are worth 10",
            Conditions = { new EventItem("Collides", ("A", "Player"), ("B", "Gem")) },
            Actions = { new EventItem("Destroy", ("Target", "B")), new EventItem("AddVar", ("Var", "Score"), ("Value", "10")) },
        });
        AddSounds(p, withMusic: true);
        p.Levels[0].Music = "Theme";
        return p;
    }

    /// <summary>Adds the standard sound effects (and optionally music) and plays them from the events.</summary>
    static void AddSounds(Project p, bool withMusic)
    {
        SoundAsset Sfx(string name, string preset, int seed = 0, double volume = 0.7) =>
            new() { Name = name, Source = "synth", Synth = Synth.Preset(preset, seed), Volume = volume };
        p.Sounds.Add(Sfx("Jump", "Jump", volume: 0.5));
        p.Sounds.Add(Sfx("Coin", "Coin", volume: 0.55));
        p.Sounds.Add(Sfx("Stomp", "Hit", volume: 0.7));
        p.Sounds.Add(Sfx("Hurt", "Explosion", 3, volume: 0.7));
        p.Sounds.Add(Sfx("Gem", "Power-up", volume: 0.5));
        p.Sounds.Add(Sfx("Spring", "Jump", 5, volume: 0.5));
        p.Sounds.Add(Sfx("Win", "Power-up", 11, volume: 0.6));
        if (withMusic)
            p.Sounds.Add(new SoundAsset { Name = "Theme", Source = "file", FileType = "wav", Data = SampleMusic.RenderWav(), Volume = 0.6 });

        EventItem Play(string sound, double vary = 0) =>
            new("PlaySound", ("Sound", sound), ("Volume", "100"), ("Pitch", "100"), ("Vary", vary.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        void AddTo(string comment, EventItem action)
        {
            foreach (var e in p.Events.Where(e => e.Comment == comment)) e.Actions.Add(action);
        }
        AddTo("Collect coins", Play("Coin", 6));
        AddTo("Gems are worth 10", Play("Gem"));
        AddTo("Stomp on enemies", Play("Stomp", 8));
        AddTo("Enemies hurt you from the side", Play("Hurt"));
        AddTo("Spikes", Play("Hurt"));
        AddTo("Falling off the world", Play("Hurt"));
        AddTo("Reach the flag", Play("Win"));
        p.Events.Insert(0, new GameEvent
        {
            Comment = "Jump sound",
            Conditions = { new EventItem("Jumps", ("A", "Player")) },
            Actions = { Play("Jump", 4) },
        });
    }

    /// <summary>A custom object made the same way a user would: own sprite, Float behaviour.</summary>
    public static ObjectType Gem()
    {
        PixelImage Frame(int shine)
        {
            var img = new PixelImage(12, 12);
            for (var y = 1; y < 11; y++)
            {
                var half = y < 5 ? y + 1 : 11 - y;
                for (var x = 6 - half; x < 6 + half; x++)
                    img[x, y] = x < 6 ? (y < 5 ? 0xFFB896F0 : 0xFF8E5BD9) : (y < 5 ? 0xFF8E5BD9 : 0xFF6A3FB8);
            }
            img.Outline(0xFF2A1748);
            if (shine >= 0) { img[3 + shine, 3] = 0xFFFFFFFF; img[4 + shine, 2] = 0xFFFFFFFF; }
            return img;
        }
        return new ObjectType
        {
            Id = "Gem", Name = "Gem", Category = "Items", Description = "A custom object: worth 10 points and floats gently.",
            Width = 12, Height = 12, Behaviours = Behaviour.Float,
            Settings = { ["FloatY"] = 3, ["Period"] = 1.6 },
            Animations = { new SpriteAnimation { Name = "Default", Fps = 6, Frames = { Frame(0), Frame(1), Frame(2), Frame(-1) } } },
        };
    }

    /// <summary>An empty project with one flat level and the standard events.</summary>
    public static Project CreateBlank()
    {
        var p = new Project { Name = "New Game", Objects = BuiltInObjects.Create() };
        var level = new Level { Name = "Level 1", Width = 40 };
        Ground(level, 0, 40, 15);
        Place(level, "Player", 3 * 16 + 2, 14 * 16 - 16);
        Place(level, "Flag", 37 * 16, 15 * 16 - 32);
        p.Levels.Add(level);
        p.Events.AddRange(DefaultEvents());
        AddSounds(p, withMusic: false);
        return p;
    }

    public static IEnumerable<GameEvent> DefaultEvents() => new[]
    {
        new GameEvent
        {
            Comment = "Collect coins",
            Conditions = { new EventItem("Collides", ("A", "Player"), ("B", "Coin")) },
            Actions = { new EventItem("Destroy", ("Target", "B")), new EventItem("AddVar", ("Var", "Score"), ("Value", "1")) },
        },
        new GameEvent
        {
            Comment = "Stomp on enemies",
            Conditions = { new EventItem("Stomps", ("A", "Player"), ("B", "Slime")) },
            Actions = { new EventItem("Destroy", ("Target", "B")), new EventItem("Bounce", ("Target", "A"), ("Power", "260")), new EventItem("AddVar", ("Var", "Score"), ("Value", "5")) },
        },
        new GameEvent
        {
            Comment = "Enemies hurt you from the side",
            Conditions = { new EventItem("Collides", ("A", "Player"), ("B", "Slime")) },
            Actions = { new EventItem("RestartLevel") },
        },
        new GameEvent
        {
            Comment = "Spikes",
            Conditions = { new EventItem("Collides", ("A", "Player"), ("B", "Spikes")) },
            Actions = { new EventItem("RestartLevel") },
        },
        new GameEvent
        {
            Comment = "Falling off the world",
            Conditions = { new EventItem("FallsOut", ("A", "Player")) },
            Actions = { new EventItem("RestartLevel") },
        },
        new GameEvent
        {
            Comment = "Reach the flag",
            Conditions = { new EventItem("Collides", ("A", "Player"), ("B", "Flag")) },
            Actions = { new EventItem("NextLevel") },
        },
    };

    static void Place(Level level, string type, double x, double y, params (string, double)[] props)
    {
        var inst = new Instance { Id = level.NextId(), Type = type, X = x, Y = y };
        foreach (var (k, v) in props) inst.Props[k] = v;
        level.Instances.Add(inst);
    }

    /// <summary>Fills a strip of ground: grass on top, dirt below, down to the level bottom.</summary>
    static void Ground(Level level, int fromTile, int toTile, int topRow)
    {
        for (var tx = fromTile; tx < toTile; tx++)
            for (var ty = topRow; ty < level.Height; ty++)
                Place(level, ty == topRow ? "Grass" : "Dirt", tx * 16, ty * 16);
    }

    static Level Level1()
    {
        var l = new Level { Name = "Green Hills", Width = 70, Height = 17, Background = "#8ED6FF" };

        Place(l, "Cloud", 4 * 16, 2 * 16);
        Place(l, "Cloud", 19 * 16, 4 * 16);
        Place(l, "Cloud", 36 * 16, 2 * 16);
        Place(l, "Cloud", 55 * 16, 3 * 16);

        Ground(l, 0, 18, 14);
        Ground(l, 22, 34, 14);
        Ground(l, 40, 52, 13);
        Ground(l, 58, 70, 14);

        Place(l, "Bush", 6 * 16, 13 * 16);
        Place(l, "Bush", 25 * 16, 13 * 16);
        Place(l, "Bush", 61 * 16, 13 * 16);

        Place(l, "Player", 2 * 16 + 2, 13 * 16);

        // first coins and a ledge to jump through
        for (var i = 0; i < 3; i++) Place(l, "Coin", (8 + i * 2) * 16 + 3, 12 * 16);
        Place(l, "Ledge", 12 * 16, 10 * 16);
        Place(l, "Coin", 13 * 16 - 5, 8 * 16 + 4);

        // gap with a moving platform
        Place(l, "MovingPlatform", 17 * 16, 12 * 16, ("DistanceX", 64), ("DistanceY", 0), ("Speed", 40));

        Place(l, "Slime", 28 * 16, 13 * 16 + 4);
        for (var x = 24; x < 34; x += 3) Place(l, "Coin", x * 16 + 3, 11 * 16);
        Place(l, "Brick", 30 * 16, 10 * 16);
        Place(l, "Brick", 31 * 16, 10 * 16);
        Place(l, "Brick", 32 * 16, 10 * 16);

        // vertical platform up to the higher ground
        Place(l, "MovingPlatform", 34 * 16, 13 * 16, ("DistanceX", 64), ("DistanceY", -48), ("Speed", 35));
        Place(l, "Spikes", 44 * 16, 12 * 16 + 8);
        Place(l, "Spikes", 45 * 16, 12 * 16 + 8);
        Place(l, "Spring", 49 * 16, 12 * 16 + 6);
        Place(l, "Gem", 49 * 16 + 2, 2 * 16);
        Place(l, "Gem", 31 * 16 + 2, 8 * 16);
        for (var y = 4; y < 10; y += 2) Place(l, "Coin", 49 * 16 + 3, y * 16);

        Place(l, "Stone", 52 * 16, 13 * 16);
        Place(l, "MovingPlatform", 52 * 16, 11 * 16, ("DistanceX", 72), ("DistanceY", 0), ("Speed", 45));
        Place(l, "Slime", 63 * 16, 13 * 16 + 4);

        Place(l, "Flag", 67 * 16, 14 * 16 - 32);
        return l;
    }

    static Level Level2()
    {
        var l = new Level { Name = "Brick Castle", Width = 50, Height = 17, Background = "#2B2F4A" };
        Ground(l, 0, 10, 14);
        Place(l, "Player", 2 * 16, 13 * 16);
        for (var x = 10; x < 50; x++)
            for (var y = 15; y < 17; y++)
                Place(l, "Stone", x * 16, y * 16);
        for (var x = 10; x < 50; x++) Place(l, "Spikes", x * 16, 14 * 16 + 8);

        Place(l, "MovingPlatform", 11 * 16, 12 * 16, ("DistanceX", 96), ("DistanceY", 0), ("Speed", 50));
        for (var x = 18; x < 24; x++) Place(l, "Brick", x * 16, 10 * 16);
        Place(l, "Slime", 20 * 16, 9 * 16 + 4);
        Place(l, "Coin", 22 * 16 + 3, 8 * 16);
        Place(l, "MovingPlatform", 25 * 16, 10 * 16, ("DistanceX", 0), ("DistanceY", -64), ("Speed", 35));
        for (var x = 29; x < 37; x++) Place(l, "Brick", x * 16, 7 * 16);
        Place(l, "Coin", 31 * 16 + 3, 5 * 16);
        Place(l, "Coin", 33 * 16 + 3, 5 * 16);
        Place(l, "Ledge", 38 * 16, 9 * 16);
        Place(l, "Ledge", 42 * 16, 11 * 16);
        for (var x = 45; x < 50; x++) Place(l, "Brick", x * 16, 12 * 16);
        Place(l, "Flag", 47 * 16, 12 * 16 - 32);
        return l;
    }
}
