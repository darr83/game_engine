using GameEngine.Core;

namespace GameEngine.Core.Tests;

public class WorldTests
{
    static Project Flat(params Instance[] extra)
    {
        var p = new Project();
        var level = new Level { Width = 30, Height = 12 };
        for (var x = 0; x < 30; x++)
            level.Instances.Add(new Instance { Id = x + 1, Type = "Grass", X = x * 16, Y = 10 * 16 });
        level.Instances.Add(new Instance { Id = 100, Type = "Player", X = 32, Y = 10 * 16 - 16 });
        foreach (var e in extra) level.Instances.Add(e);
        p.Levels.Add(level);
        p.Events.AddRange(SampleProject.DefaultEvents());
        return p;
    }

    static Entity Player(World w) => w.Entities.First(e => e.Type == "Player");

    static void Run(World w, int ticks, Action? input = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            input?.Invoke();
            w.Step();
        }
    }

    [Fact]
    public void Player_stands_on_solid_ground()
    {
        var w = new World(Flat());
        Run(w, 60);
        var p = Player(w);
        Assert.True(p.OnGround);
        Assert.Equal(10 * 16 - p.H, p.Y, 3);
    }

    [Fact]
    public void Player_runs_right_when_key_held()
    {
        var w = new World(Flat());
        w.Input.KeyDown("Right");
        Run(w, 60);
        Assert.True(Player(w).X > 100);
    }

    [Fact]
    public void Player_jumps_and_lands()
    {
        var w = new World(Flat());
        Run(w, 5);
        w.Input.KeyDown("Space");
        Run(w, 10);
        Assert.True(Player(w).Y < 10 * 16 - 40, "player should be in the air");
        w.Input.KeyUp("Space");
        Run(w, 120);
        Assert.True(Player(w).OnGround);
    }

    [Fact]
    public void Walls_block_the_player()
    {
        var w = new World(Flat(new Instance { Id = 200, Type = "Brick", X = 80, Y = 10 * 16 - 16 }));
        w.Input.KeyDown("Right");
        Run(w, 120);
        Assert.True(Player(w).X + Player(w).W <= 80 + 0.001);
    }

    [Fact]
    public void Touching_a_coin_destroys_it_and_adds_score()
    {
        var w = new World(Flat(new Instance { Id = 200, Type = "Coin", X = 60, Y = 10 * 16 - 12 }));
        w.Input.KeyDown("Right");
        Run(w, 60);
        Assert.DoesNotContain(w.Entities, e => e.Type == "Coin");
        Assert.Equal(1, w.Vars["Score"]);
    }

    [Fact]
    public void Spikes_restart_the_level_and_reset_score()
    {
        var w = new World(Flat(
            new Instance { Id = 200, Type = "Coin", X = 50, Y = 10 * 16 - 12 },
            new Instance { Id = 201, Type = "Spikes", X = 120, Y = 10 * 16 - 8 }));
        w.Input.KeyDown("Right");
        var collected = false;
        for (var i = 0; i < 200 && w.Restarts == 0; i++)
        {
            w.Step();
            collected |= w.Vars["Score"] == 1;
        }
        Assert.True(collected);
        Assert.Equal(1, w.Restarts);
        Assert.Equal(0, w.Vars["Score"]);
    }

    [Fact]
    public void Player_rides_a_moving_platform()
    {
        var p = new Project();
        var level = new Level { Width = 40, Height = 12 };
        level.Instances.Add(new Instance
        {
            Id = 1, Type = "MovingPlatform", X = 32, Y = 100,
            Props = { ["DistanceX"] = 160, ["DistanceY"] = 0, ["Speed"] = 60 },
        });
        level.Instances.Add(new Instance { Id = 2, Type = "Player", X = 40, Y = 84 });
        p.Levels.Add(level);

        var w = new World(p);
        Run(w, 120);
        var player = Player(w);
        var platform = w.Entities.First(e => e.Type == "MovingPlatform");
        Assert.True(player.OnGround);
        Assert.Same(platform, player.StandingOn);
        Assert.True(player.X > 70, $"player should be carried, x={player.X}");
    }

    [Fact]
    public void Stomping_a_slime_destroys_it_without_restarting()
    {
        var w = new World(Flat(new Instance { Id = 200, Type = "Slime", X = 34, Y = 60, Props = { ["Speed"] = 0 } }));
        // drop the player onto the slime from above
        var player = Player(w);
        player.Y = 0;
        Run(w, 60);
        Assert.DoesNotContain(w.Entities, e => e.Type == "Slime");
        Assert.Equal(0, w.Restarts);
        Assert.Equal(5, w.Vars["Score"]);
    }

    [Fact]
    public void Reaching_the_flag_on_the_last_level_wins()
    {
        var w = new World(Flat(new Instance { Id = 200, Type = "Flag", X = 90, Y = 10 * 16 - 32 }));
        w.Input.KeyDown("Right");
        Run(w, 90);
        Assert.True(w.Finished);
        Assert.Equal("You win!", w.Message);
    }

    [Fact]
    public void Sample_project_round_trips_through_json()
    {
        var p = SampleProject.Create();
        var copy = ProjectIO.FromJson(ProjectIO.ToJson(p));
        Assert.Equal(p.Levels.Count, copy.Levels.Count);
        Assert.Equal(p.Levels[0].Instances.Count, copy.Levels[0].Instances.Count);
        Assert.Equal(p.Events.Count, copy.Events.Count);
        Assert.Equal(64, copy.Levels[0].Instances.First(i => i.Type == "MovingPlatform").Props["DistanceX"]);
    }

    [Fact]
    public void Every_catalog_object_has_sprite_art_of_the_right_size()
    {
        foreach (var def in BuiltInObjects.Create())
        {
            Assert.NotEmpty(def.Animations);
            foreach (var f in def.Animations.SelectMany(a => a.Frames))
            {
                Assert.Equal(def.Width, f.Width);
                Assert.Equal(def.Height, f.Height);
            }
        }
    }

    [Fact]
    public void Every_event_kind_describes_itself()
    {
        foreach (var k in EventCatalog.Conditions.Concat(EventCatalog.Actions))
        {
            var text = k.Describe(k.CreateDefault());
            Assert.DoesNotContain("{", text);
        }
    }
}
