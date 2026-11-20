using GameEngine.Core;

namespace GameEngine.Core.Tests;

public class CustomObjectTests
{
    static Project FlatWith(params ObjectType[] types)
    {
        var p = new Project();
        p.EnsureObjects();
        p.Objects.AddRange(types);
        var level = new Level { Width = 30, Height = 12 };
        for (var x = 0; x < 30; x++)
            level.Instances.Add(new Instance { Id = x + 1, Type = "Grass", X = x * 16, Y = 10 * 16 });
        p.Levels.Add(level);
        return p;
    }

    static ObjectType Custom(string id, Behaviour b, int w = 8, int h = 8) => new()
    {
        Id = id, Name = id, Width = w, Height = h, Behaviours = b,
        Animations = { new SpriteAnimation { Name = "Default", Frames = { new PixelImage(w, h) } } },
    };

    [Fact]
    public void Double_jump_needs_two_jumps_setting()
    {
        foreach (var (jumps, expectHigher) in new[] { (1.0, false), (2.0, true) })
        {
            var p = FlatWith();
            p.Levels[0].Instances.Add(new Instance { Id = 100, Type = "Player", X = 32, Y = 144, Props = { ["Jumps"] = jumps } });
            var w = new World(p);
            for (var i = 0; i < 10; i++) w.Step();
            w.Input.KeyDown("Space");
            for (var i = 0; i < 15; i++) w.Step();
            w.Input.KeyUp("Space");
            w.Step();
            w.Input.KeyDown("Space");      // second press while in the air
            var lowest = double.MaxValue;
            for (var i = 0; i < 30; i++) { w.Step(); lowest = Math.Min(lowest, w.Entities.First(e => e.Type == "Player").Y); }
            Assert.Equal(expectHigher, lowest < 70);   // one jump peaks near y=94, two near y=46
        }
    }

    [Fact]
    public void Bullet_flies_at_its_angle_and_is_destroyed_by_walls()
    {
        var bullet = Custom("Fireball", Behaviour.Bullet);
        bullet.Settings["Speed"] = 120;
        bullet.Settings["Angle"] = 0;
        var p = FlatWith(bullet);
        p.Levels[0].Instances.Add(new Instance { Id = 100, Type = "Fireball", X = 20, Y = 100 });
        p.Levels[0].Instances.Add(new Instance { Id = 101, Type = "Brick", X = 96, Y = 96 });
        var w = new World(p);
        w.Step();
        Assert.True(w.Entities.First(e => e.Type == "Fireball").X > 20);
        for (var i = 0; i < 60; i++) w.Step();
        Assert.DoesNotContain(w.Entities, e => e.Type == "Fireball");
    }

    [Fact]
    public void Object_variables_are_per_object_and_drive_events()
    {
        var boss = Custom("Boss", Behaviour.None, 16, 16);
        boss.Variables["Health"] = 3;
        var p = FlatWith(boss);
        p.Levels[0].Instances.Add(new Instance { Id = 100, Type = "Boss", X = 40, Y = 100 });
        p.Levels[0].Instances.Add(new Instance { Id = 101, Type = "Boss", X = 200, Y = 100 });
        // objects whose own Health reaches 0 are destroyed
        p.Events.Add(new GameEvent
        {
            Conditions = { new EventItem("CompareObjectVar", ("A", "Boss"), ("Var", "Health"), ("Op", "<="), ("Value", "0")) },
            Actions = { new EventItem("Destroy", ("Target", "A")) },
        });
        var w = new World(p);
        var first = w.Entities.First(e => e.Type == "Boss");
        first.Vars["Health"] = 0;
        w.Step();
        Assert.Single(w.Entities, e => e.Type == "Boss");
        Assert.Equal(3, w.Entities.Single(e => e.Type == "Boss").Vars["Health"]);
    }

    [Fact]
    public void Animations_are_chosen_from_movement()
    {
        var p = FlatWith();
        p.Levels[0].Instances.Add(new Instance { Id = 100, Type = "Player", X = 32, Y = 144 });
        var w = new World(p);
        var player = w.Entities.First(e => e.Type == "Player");
        var type = p.FindType("Player")!;
        w.Step();
        Assert.Same(type.Animation("Idle")!.Frames[0], World.SpriteFor(player).image);
        w.Input.KeyDown("Left");
        for (var i = 0; i < 10; i++) w.Step();
        var (img, mirrored) = World.SpriteFor(player);
        Assert.Contains(img, type.Animation("Run")!.Frames);
        Assert.True(mirrored);
    }

    [Fact]
    public void Custom_sprites_survive_saving_and_loading()
    {
        var bat = Custom("Bat", Behaviour.Float, 4, 3);
        bat.Animations[0].Frames[0][1, 2] = 0xFF112233;
        bat.Animations[0].Frames[0][3, 0] = 0x80FFFFFF;
        var p = FlatWith(bat);
        var copy = ProjectIO.FromJson(ProjectIO.ToJson(p)).FindType("Bat")!;
        Assert.Equal(Behaviour.Float, copy.Behaviours);
        Assert.Equal(0xFF112233, copy.Animations[0].Frames[0][1, 2]);
        Assert.Equal(0x80FFFFFF, copy.Animations[0].Frames[0][3, 0]);
        Assert.Equal(0u, copy.Animations[0].Frames[0][0, 0]);
    }

    [Fact]
    public void Old_projects_without_object_types_get_the_premade_ones()
    {
        const string json = """{ "Name": "Old", "Levels": [ { "Instances": [ { "Id": 1, "Type": "Player", "X": 0, "Y": 0 } ] } ] }""";
        var p = ProjectIO.FromJson(json);
        Assert.NotNull(p.FindType("Player"));
        Assert.NotNull(p.FindType("Coin"));
    }

    [Fact]
    public void Editing_a_premade_type_changes_how_it_plays()
    {
        var p = FlatWith();
        p.FindType("Player")!.Settings["Speed"] = 300;
        p.Levels[0].Instances.Add(new Instance { Id = 100, Type = "Player", X = 32, Y = 144 });
        var w = new World(p);
        w.Input.KeyDown("Right");
        for (var i = 0; i < 60; i++) w.Step();
        Assert.True(w.Entities.First(e => e.Type == "Player").X > 250);
    }

    [Fact]
    public void Resizing_keeps_art_at_the_bottom_centre()
    {
        var t = Custom("Box", Behaviour.Solid, 2, 2);
        t.Animations[0].Frames[0][0, 1] = 0xFFFF0000;
        t.Resize(4, 4);
        Assert.Equal(4, t.Animations[0].Frames[0].Width);
        Assert.Equal(0xFFFF0000, t.Animations[0].Frames[0][1, 3]);
    }

    [Fact]
    public void New_type_ids_are_unique()
    {
        var p = FlatWith();
        Assert.Equal("Coin2", p.NewTypeId("Coin"));
        Assert.Equal("BigBat", p.NewTypeId("Big Bat!"));
    }
}
