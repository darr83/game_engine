using System.Globalization;

namespace GameEngine.Core;

/// <summary>Keyboard state for one tick. Key names match <see cref="EventCatalog.Keys"/>.</summary>
public sealed class InputState
{
    readonly HashSet<string> _down = new();
    readonly HashSet<string> _pressed = new();

    public void KeyDown(string key)
    {
        if (_down.Add(key)) _pressed.Add(key);
    }

    public void KeyUp(string key) => _down.Remove(key);
    public bool IsDown(string key) => _down.Contains(key);
    public bool WasPressed(string key) => _pressed.Contains(key);
    public void EndTick() => _pressed.Clear();
    public void Clear() { _down.Clear(); _pressed.Clear(); }

    public bool Left => IsDown("Left") || IsDown("A");
    public bool Right => IsDown("Right") || IsDown("D");
    public bool JumpHeld => IsDown("Space") || IsDown("Up") || IsDown("W") || IsDown("Z");
    public bool JumpPressed => WasPressed("Space") || WasPressed("Up") || WasPressed("W") || WasPressed("Z");
}

/// <summary>A live object while the game runs.</summary>
public sealed class Entity
{
    public int Id { get; init; }
    public required ObjectType Def { get; init; }
    public required Instance Source { get; init; }
    public string Type => Def.Id;

    public double X, Y, VX, VY;
    public double W => Def.Width;
    public double H => Def.Height;
    public double PrevX, PrevY;

    public int Facing = 1;
    public bool Visible = true;
    public bool OnGround;
    public int JumpsUsed;

    /// <summary>This object's own variables (e.g. Health), started from its type's defaults.</summary>
    public Dictionary<string, double> Vars { get; init; } = new();

    /// <summary>An animation chosen by an event; overrides the automatic one.</summary>
    public string? Animation;
    public Entity? StandingOn;
    public bool Dead;
    public double AnimTime;

    // moving platform state
    public double StartX, StartY, Progress, Direction = 1, DeltaX, DeltaY;
    // player state
    public double Coyote, JumpBuffer;
    // spring state
    public double Squash;

    public double Prop(string name) => Source.Get(Def, name);

    public bool Overlaps(Entity o) =>
        X < o.X + o.W && X + W > o.X && Y < o.Y + o.H && Y + H > o.Y;

    public bool Overlaps(double x, double y, double w, double h) =>
        X < x + w && X + W > x && Y < y + h && Y + H > y;
}

/// <summary>
/// Runs a project: physics, behaviours and the event sheet. Has no UI dependencies,
/// so the same code drives the editor's play mode, the exported game and the tests.
/// </summary>
public sealed class World
{
    public const double TickRate = 60;
    public const double Dt = 1 / TickRate;
    const double MaxFall = 520;

    readonly Project _project;
    readonly List<Entity> _entities = new();
    readonly List<Entity> _spawned = new();
    readonly Dictionary<int, double> _timers = new();
    readonly Random _random = new(1234);
    Dictionary<string, double> _varsAtLevelStart = new();
    double _accumulator;
    long _tick;
    int _nextId;
    PendingChange _pending;

    enum PendingChange { None, Restart, Next }

    public World(Project project, int levelIndex = 0)
    {
        _project = project;
        project.EnsureObjects();
        Vars = new Dictionary<string, double>(project.Variables);
        LevelIndex = Math.Clamp(levelIndex, 0, Math.Max(0, project.Levels.Count - 1));
        LoadLevel();
    }

    public Project Project => _project;
    public InputState Input { get; } = new();
    public Dictionary<string, double> Vars { get; }
    public int LevelIndex { get; private set; }
    public Level Level => _project.Levels[LevelIndex];
    public IReadOnlyList<Entity> Entities => _entities;
    public double LevelWidth => Level.Width * _project.TileSize;
    public double LevelHeight => Level.Height * _project.TileSize;

    /// <summary>Size of the visible area in world pixels; set by the host window.</summary>
    public double ViewWidth { get; set; } = 400;
    public double ViewHeight { get; set; } = 272;
    public double CameraX { get; private set; }
    public double CameraY { get; private set; }

    public string? Message { get; private set; }
    public double MessageTime { get; private set; }
    public bool Finished { get; private set; }
    public long Tick => _tick;
    public int Restarts { get; private set; }

    /// <summary>Advances by real elapsed time using fixed 60 Hz ticks.</summary>
    public void Update(double elapsedSeconds)
    {
        _accumulator += Math.Min(elapsedSeconds, 0.25);
        var steps = 0;
        while (_accumulator >= Dt && steps < 8)
        {
            Step();
            _accumulator -= Dt;
            steps++;
        }
    }

    public void Step()
    {
        if (Finished) { Input.EndTick(); return; }

        foreach (var e in _entities)
        {
            e.PrevX = e.X;
            e.PrevY = e.Y;
            e.AnimTime += Dt;
        }

        UpdatePlatforms();
        foreach (var e in _entities)
        {
            if (e.Dead) continue;
            if (e.Def.Has(Behaviour.Platformer)) UpdatePlayer(e);
            else if (e.Def.Has(Behaviour.Patrol)) UpdatePatrol(e);
            else if (e.Def.Has(Behaviour.Bullet)) UpdateBullet(e);
            else if (e.Def.Has(Behaviour.Gravity)) UpdateFalling(e);
            if (e.Def.Has(Behaviour.Float)) UpdateFloat(e);
            if (e.Squash > 0) e.Squash -= Dt;
        }

        RunEvents();
        _tick++;

        _entities.RemoveAll(e => e.Dead);
        _entities.AddRange(_spawned);
        _spawned.Clear();

        if (MessageTime > 0)
        {
            MessageTime -= Dt;
            if (MessageTime <= 0 && !Finished) Message = null;
        }

        switch (_pending)
        {
            case PendingChange.Restart:
                Restarts++;
                foreach (var kv in _varsAtLevelStart) Vars[kv.Key] = kv.Value;
                LoadLevel();
                break;
            case PendingChange.Next:
                if (LevelIndex + 1 < _project.Levels.Count)
                {
                    LevelIndex++;
                    LoadLevel();
                }
                else
                {
                    Finished = true;
                    Message = "You win!";
                    MessageTime = double.MaxValue;
                }
                break;
        }
        _pending = PendingChange.None;

        UpdateCamera();
        Input.EndTick();
    }

    void LoadLevel()
    {
        _entities.Clear();
        _timers.Clear();
        _tick = 0;
        _nextId = 1;
        _varsAtLevelStart = new Dictionary<string, double>(Vars);
        foreach (var inst in Level.Instances)
        {
            var def = _project.FindType(inst.Type);
            if (def is null) continue;
            _entities.Add(CreateEntity(def, inst, inst.X, inst.Y));
        }
        UpdateCamera();
    }

    Entity CreateEntity(ObjectType def, Instance source, double x, double y)
    {
        var e = new Entity
        {
            Id = _nextId++,
            Def = def,
            Source = source,
            X = x, Y = y, PrevX = x, PrevY = y,
            StartX = x, StartY = y,
            Vars = new Dictionary<string, double>(def.Variables),
        };
        if (def.Has(Behaviour.Bullet))
        {
            var angle = e.Prop("Angle") * Math.PI / 180;
            e.VX = Math.Cos(angle) * e.Prop("Speed");
            e.VY = Math.Sin(angle) * e.Prop("Speed");
            if (e.VX < -0.01) e.Facing = -1;
        }
        return e;
    }

    // ------------------------------------------------------------------ behaviours

    void UpdatePlatforms()
    {
        foreach (var p in _entities)
        {
            if (!p.Def.Has(Behaviour.MovingPlatform)) continue;
            double dx = p.Prop("DistanceX"), dy = p.Prop("DistanceY");
            var length = Math.Sqrt(dx * dx + dy * dy);
            p.DeltaX = p.DeltaY = 0;
            if (length < 0.5) continue;

            p.Progress += p.Direction * p.Prop("Speed") * Dt / length;
            if (p.Progress >= 1) { p.Progress = 1; p.Direction = -1; }
            if (p.Progress <= 0) { p.Progress = 0; p.Direction = 1; }

            // ease in and out at the ends so riders don't get flung off
            var t = (1 - Math.Cos(p.Progress * Math.PI)) / 2;
            var nx = p.StartX + dx * t;
            var ny = p.StartY + dy * t;
            p.DeltaX = nx - p.X;
            p.DeltaY = ny - p.Y;
            p.X = nx;
            p.Y = ny;

            // carry anything standing on it
            foreach (var r in _entities)
                if (r.StandingOn == p)
                {
                    r.X += p.DeltaX;
                    r.Y += p.DeltaY;
                }
        }
    }

    void UpdatePlayer(Entity e)
    {
        var speed = e.Prop("Speed");
        var gravity = e.Prop("Gravity");
        var jump = e.Prop("JumpPower");

        var move = (Input.Right ? 1 : 0) - (Input.Left ? 1 : 0);
        if (move != 0) e.Facing = move;
        var a = e.Prop("Acceleration");
        if (a <= 0) a = 12;
        var accel = speed * (e.OnGround ? a : a * 0.6);
        e.VX = Approach(e.VX, move * speed, accel * Dt);

        if (e.OnGround) e.JumpsUsed = 0;
        e.Coyote = e.OnGround ? 0.1 : e.Coyote - Dt;
        e.JumpBuffer = Input.JumpPressed ? 0.12 : e.JumpBuffer - Dt;
        var maxJumps = Math.Max(1, (int)e.Prop("Jumps"));
        // walking off a ledge uses up the ground jump
        var used = e.Coyote <= 0 ? Math.Max(1, e.JumpsUsed) : e.JumpsUsed;
        var groundJump = e.JumpBuffer > 0 && e.Coyote > 0;
        var airJump = !groundJump && Input.JumpPressed && e.Coyote <= 0 && used < maxJumps;
        if (groundJump || airJump)
        {
            e.VY = -jump;
            e.JumpBuffer = 0;
            e.Coyote = 0;
            e.JumpsUsed = used + 1;
            e.OnGround = false;
            e.StandingOn = null;
        }

        // releasing jump early gives a shorter hop
        var g = gravity * (e.VY < 0 && !Input.JumpHeld ? 2.5 : 1);
        e.VY = Math.Min(e.VY + g * Dt, MaxFall);

        MoveAndCollide(e, out _);

        // springs
        foreach (var s in _entities)
        {
            if (!s.Def.Has(Behaviour.Spring) || s.Dead) continue;
            if (e.VY >= 0 && e.Overlaps(s) && e.PrevY + e.H <= s.Y + 4)
            {
                e.Y = s.Y - e.H;
                e.VY = -s.Prop("Power");
                e.OnGround = false;
                e.StandingOn = null;
                s.Squash = 0.2;
            }
        }

        // keep inside the level horizontally
        if (e.X < 0) { e.X = 0; e.VX = 0; }
        if (e.X + e.W > LevelWidth) { e.X = LevelWidth - e.W; e.VX = 0; }
    }

    void UpdatePatrol(Entity e)
    {
        var speed = e.Prop("Speed");
        e.VX = e.Facing * speed;
        e.VY = Math.Min(e.VY + e.Prop("Gravity") * Dt, MaxFall);

        MoveAndCollide(e, out var hitWall);

        var turn = hitWall || e.X <= 0 || e.X + e.W >= LevelWidth;
        if (!turn && e.OnGround && e.Prop("TurnAtLedges") >= 0.5)
        {
            // turn around at ledges
            var aheadX = e.Facing > 0 ? e.X + e.W + 1 : e.X - 2;
            turn = !_entities.Any(b => IsBlocker(b, e) && b.Overlaps(aheadX, e.Y + e.H + 1, 1, 2));
        }
        if (turn) e.Facing = -e.Facing;
    }

    void UpdateBullet(Entity e)
    {
        e.X += e.VX * Dt;
        e.Y += e.VY * Dt;
        if (e.Prop("StopAtSolids") >= 0.5 && _entities.Any(b => b != e && !b.Dead && b.Def.Has(Behaviour.Solid) && e.Overlaps(b)))
            e.Dead = true;
        // tidy up bullets that have flown far outside the level
        if (e.X < -200 || e.X > LevelWidth + 200 || e.Y < -200 || e.Y > LevelHeight + 200) e.Dead = true;
    }

    void UpdateFalling(Entity e)
    {
        e.VX = Approach(e.VX, 0, 600 * Dt);
        e.VY = Math.Min(e.VY + e.Prop("Gravity") * Dt, MaxFall);
        MoveAndCollide(e, out _);
    }

    void UpdateFloat(Entity e)
    {
        var period = Math.Max(0.1, e.Prop("Period"));
        var offset = e.StartX * 0.05;    // so neighbours don't bob in perfect sync
        var phase = Math.Sin(_tickTime * 2 * Math.PI / period + offset);
        var prevPhase = Math.Sin((_tickTime - Dt) * 2 * Math.PI / period + offset);
        e.X += (phase - prevPhase) * e.Prop("FloatX");
        e.Y += (phase - prevPhase) * e.Prop("FloatY");
    }

    double _tickTime => _tick * Dt;

    static double Approach(double value, double target, double step) =>
        value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);

    static bool IsBlocker(Entity b, Entity mover) => b != mover && !b.Dead && b.Def.BlocksMovement;

    /// <summary>Moves one axis at a time and pushes the mover out of solids.</summary>
    void MoveAndCollide(Entity e, out bool hitWall)
    {
        hitWall = false;

        e.X += e.VX * Dt;
        foreach (var b in _entities)
        {
            if (!IsBlocker(b, e) || !b.Def.Has(Behaviour.Solid) || !e.Overlaps(b)) continue;
            if (e.X + e.W / 2 < b.X + b.W / 2) e.X = b.X - e.W; else e.X = b.X + b.W;
            e.VX = 0;
            hitWall = true;
        }

        var prevBottom = e.Y + e.H;
        e.Y += e.VY * Dt;
        e.OnGround = false;
        e.StandingOn = null;
        foreach (var b in _entities)
        {
            if (!IsBlocker(b, e) || !e.Overlaps(b)) continue;
            // where the blocker's top was before it moved this tick
            var blockerPrevTop = b.Y - b.DeltaY;
            var landing = e.VY >= 0 && prevBottom <= blockerPrevTop + 0.5 + Math.Max(0, b.DeltaY);
            if (b.Def.Has(Behaviour.JumpThru))
            {
                if (!landing) continue;
            }
            else if (!landing && e.VY < 0)
            {
                e.Y = b.Y + b.H;
                e.VY = 0;
                continue;
            }
            else if (!landing)
            {
                continue;
            }
            e.Y = b.Y - e.H;
            e.VY = 0;
            e.OnGround = true;
            e.StandingOn = b;
        }
    }

    void UpdateCamera()
    {
        var player = _entities.FirstOrDefault(e => e.Def.Has(Behaviour.Platformer));
        if (player is not null)
        {
            CameraX = player.X + player.W / 2 - ViewWidth / 2;
            CameraY = player.Y + player.H / 2 - ViewHeight / 2;
        }
        CameraX = Math.Clamp(CameraX, 0, Math.Max(0, LevelWidth - ViewWidth));
        CameraY = Math.Clamp(CameraY, 0, Math.Max(0, LevelHeight - ViewHeight));
    }

    // ------------------------------------------------------------------ event sheet

    readonly record struct Binding(Entity? A, Entity? B);

    IEnumerable<Entity> Alive(string type) => _entities.Where(e => !e.Dead && e.Type == type);

    void RunEvents()
    {
        for (var i = 0; i < _project.Events.Count; i++)
        {
            var ev = _project.Events[i];
            if (!ev.Enabled || ev.Actions.Count == 0) continue;

            var bindings = new List<Binding> { new(null, null) };
            foreach (var c in ev.Conditions)
            {
                bindings = Filter(i, c, bindings);
                if (bindings.Count == 0) break;
            }

            foreach (var b in bindings)
                foreach (var a in ev.Actions)
                    RunAction(a, b);
        }
    }

    List<Binding> Filter(int eventIndex, EventItem c, List<Binding> bindings)
    {
        List<Binding> Pairs(Func<Entity, Entity, bool> test)
        {
            var result = new List<Binding>();
            string ta = c.Arg("A"), tb = c.Arg("B");
            foreach (var bind in bindings)
                foreach (var a in Alive(ta))
                {
                    if (bind.A is not null && bind.A != a) continue;
                    foreach (var o in Alive(tb))
                        if (o != a && !a.Dead && !o.Dead && test(a, o))
                            result.Add(new Binding(a, o));
                }
            return result;
        }

        List<Binding> Singles(Func<Entity, bool> test)
        {
            var result = new List<Binding>();
            foreach (var bind in bindings)
                foreach (var a in Alive(c.Arg("A")))
                    if ((bind.A is null || bind.A == a) && test(a))
                        result.Add(new Binding(a, bind.B));
            return result;
        }

        List<Binding> If(bool ok) => ok ? bindings : new List<Binding>();

        switch (c.Kind)
        {
            case "OnStart": return If(_tick == 0);
            case "EveryTick": return bindings;
            case "EverySeconds":
            {
                var t = _timers.GetValueOrDefault(eventIndex) + Dt;
                var fire = t >= Math.Max(Dt, c.Num("Seconds", 1));
                _timers[eventIndex] = fire ? 0 : t;
                return If(fire);
            }
            case "Collides": return Pairs((a, b) => a.Overlaps(b));
            case "Stomps": return Pairs((a, b) => a.Overlaps(b) && a.VY > 0 && a.PrevY + a.H <= b.Y + b.H * 0.5);
            case "FallsOut": return Singles(a => a.Y > LevelHeight + 16);
            case "IsOnGround": return Singles(a => a.OnGround);
            case "IsMoving": return Singles(a => Math.Abs(a.X - a.PrevX) > 0.01 || Math.Abs(a.Y - a.PrevY) > 0.01);
            case "CompareObjectVar": return Singles(a => Compare(a.Vars.GetValueOrDefault(c.Arg("Var")), c.Arg("Op", "="), c.Num("Value")));
            case "Random": return If(_random.NextDouble() * 100 < c.Num("Percent", 50));
            case "CountIs": return If(Compare(Alive(c.Arg("Type")).Count(), c.Arg("Op", "="), c.Num("Value")));
            case "KeyPressed": return If(Input.WasPressed(c.Arg("Key")));
            case "KeyDown": return If(Input.IsDown(c.Arg("Key")));
            case "Compare": return If(Compare(Vars.GetValueOrDefault(c.Arg("Var")), c.Arg("Op", "="), c.Num("Value")));
            default: return new List<Binding>();
        }
    }

    static bool Compare(double a, string op, double b) => op switch
    {
        "=" => Math.Abs(a - b) < 1e-9,
        "!=" => Math.Abs(a - b) >= 1e-9,
        "<" => a < b,
        "<=" => a <= b,
        ">" => a > b,
        ">=" => a >= b,
        _ => false,
    };

    void RunAction(EventItem a, Binding b)
    {
        Entity? Target() => a.Arg("Target", "A") == "B" ? b.B : b.A;

        switch (a.Kind)
        {
            case "Destroy":
                if (Target() is { } d) d.Dead = true;
                break;
            case "Spawn":
            {
                var def = _project.FindType(a.Arg("Type"));
                var at = Target();
                if (def is null) break;
                double x = at is null ? CameraX + ViewWidth / 2 : at.X + at.W / 2 - def.Width / 2.0;
                double y = at is null ? CameraY + ViewHeight / 2 : at.Y + at.H - def.Height;
                _spawned.Add(CreateEntity(def, new Instance { Type = def.Id }, x, y));
                break;
            }
            case "Bounce":
                if (Target() is { } t)
                {
                    t.VY = -a.Num("Power", 250);
                    t.OnGround = false;
                    t.StandingOn = null;
                }
                break;
            case "SetObjectVar":
                if (Target() is { } sv) sv.Vars[a.Arg("Var")] = a.Num("Value");
                break;
            case "AddObjectVar":
                if (Target() is { } av) av.Vars[a.Arg("Var")] = av.Vars.GetValueOrDefault(a.Arg("Var")) + a.Num("Value");
                break;
            case "SetVelocity":
                if (Target() is { } vt)
                {
                    vt.VX = a.Num("VX");
                    vt.VY = a.Num("VY");
                    if (vt.VY < 0) { vt.OnGround = false; vt.StandingOn = null; }
                }
                break;
            case "MoveBy":
                if (Target() is { } mt) { mt.X += a.Num("X"); mt.Y += a.Num("Y"); }
                break;
            case "SetAnimation":
                if (Target() is { } at2) { at2.Animation = a.Arg("Animation"); at2.AnimTime = 0; }
                break;
            case "Flip":
                if (Target() is { } ft) ft.Facing = -ft.Facing;
                break;
            case "SetVisible":
                if (Target() is { } vis) vis.Visible = a.Arg("Visible", "Yes") != "No";
                break;
            case "AddVar":
                Vars[a.Arg("Var")] = Vars.GetValueOrDefault(a.Arg("Var")) + a.Num("Value");
                break;
            case "SetVar":
                Vars[a.Arg("Var")] = a.Num("Value");
                break;
            case "ShowMessage":
                Message = FormatText(a.Arg("Text"));
                MessageTime = a.Num("Seconds", 2);
                break;
            case "RestartLevel":
                if (_pending == PendingChange.None) _pending = PendingChange.Restart;
                break;
            case "NextLevel":
                _pending = PendingChange.Next;
                break;
        }
    }

    /// <summary>Replaces {VariableName} with the variable's value.</summary>
    public string FormatText(string text)
    {
        foreach (var kv in Vars)
            text = text.Replace("{" + kv.Key + "}", kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
        return text;
    }

    /// <summary>The image to draw for an entity right now, and whether to flip it.</summary>
    public static (PixelImage image, bool mirrored) SpriteFor(Entity e)
    {
        var t = e.Def;
        SpriteAnimation? anim = null;
        if (e.Animation is { } forced) anim = t.Animation(forced);
        if (anim is null && (t.Has(Behaviour.Platformer) || t.Has(Behaviour.Patrol) || t.Has(Behaviour.Gravity)))
        {
            if (!e.OnGround) anim = (e.VY > 0 ? t.Animation("Fall") : null) ?? t.Animation("Jump");
            else if (Math.Abs(e.VX) > 5) anim = t.Animation("Run") ?? t.Animation("Walk");
            anim ??= t.Animation("Idle");
        }
        anim ??= t.Animation("Default") ?? t.Animations.FirstOrDefault(a => a.Frames.Count > 0);
        if (anim is null) return (t.Icon, false);

        var count = anim.Frames.Count;
        var index = anim.Fps <= 0 || count == 1 ? 0 : (int)(e.AnimTime * anim.Fps);
        index = anim.Loop ? index % count : Math.Min(index, count - 1);
        var flip = e.Facing < 0 && (t.Has(Behaviour.Platformer) || t.Has(Behaviour.Patrol) || t.Has(Behaviour.Bullet) || e.Animation is not null);
        return (anim.Frames[index], flip);
    }
}
