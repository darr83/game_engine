using GameEngine.Core;

namespace GameEngine.Core.Tests;

public class CollisionBoxTests
{
    static ObjectType Crate(int boxX, int boxY, int boxW, int boxH)
    {
        var t = new ObjectType
        {
            Id = "Crate", Name = "Crate", Width = 16, Height = 16, Behaviours = Behaviour.Gravity,
            Animations = { new SpriteAnimation { Frames = { new PixelImage(16, 16) } } },
        };
        t.SetBox(boxX, boxY, boxW, boxH);
        return t;
    }

    static World FlatWorld(ObjectType type, double x, double y)
    {
        var p = new Project();
        p.EnsureObjects();
        p.Objects.Add(type);
        var level = new Level { Width = 20, Height = 12 };
        for (var i = 0; i < 20; i++) level.Instances.Add(new Instance { Id = i + 1, Type = "Grass", X = i * 16, Y = 160 });
        level.Instances.Add(new Instance { Id = 100, Type = type.Id, X = x, Y = y });
        p.Levels.Add(level);
        return new World(p);
    }

    [Fact]
    public void Whole_sprite_is_the_default_box()
    {
        var t = Crate(0, 0, 16, 16);
        Assert.Equal(0, t.CollisionWidth);         // stored as "whole sprite"
        Assert.Equal((0, 0, 16, 16), t.Box);
        Assert.False(t.HasCustomBox);
    }

    [Fact]
    public void Box_is_clamped_inside_the_sprite()
    {
        var t = Crate(10, 12, 50, 50);
        Assert.Equal((10, 12, 6, 4), t.Box);
    }

    [Fact]
    public void Objects_rest_on_their_box_not_their_sprite()
    {
        // box ends 4 px above the sprite's bottom, so the sprite overlaps the ground by 4 px
        var w = FlatWorld(Crate(2, 0, 12, 12), 32, 40);
        for (var i = 0; i < 120; i++) w.Step();
        var e = w.Entities.First(x => x.Type == "Crate");
        Assert.True(e.OnGround);
        Assert.Equal(160 - 12, e.Y, 3);
        Assert.Equal(160 - 12, e.SpritePosition(false).Y, 3);
        Assert.Equal(e.X - 2, e.SpritePosition(false).X, 3);
    }

    [Fact]
    public void Placed_objects_keep_their_sprite_position()
    {
        var w = FlatWorld(Crate(3, 5, 8, 8), 48, 64);
        var e = w.Entities.First(x => x.Type == "Crate");
        Assert.Equal((48.0, 64.0), e.SpritePosition(false));
        Assert.Equal(51, e.X);
        Assert.Equal(69, e.Y);
    }

    [Fact]
    public void Mirrored_sprites_stay_over_an_off_centre_box()
    {
        var w = FlatWorld(Crate(0, 0, 10, 16), 48, 64);
        var e = w.Entities.First(x => x.Type == "Crate");
        Assert.Equal(48, e.SpritePosition(false).X);
        // flipped: the box is now on the sprite's right-hand side
        Assert.Equal(42, e.SpritePosition(true).X);
    }

    [Fact]
    public void Resizing_moves_the_box_with_the_art()
    {
        var t = Crate(2, 4, 8, 8);
        t.Resize(20, 20);
        Assert.Equal((4, 8, 8, 8), t.Box);
    }

    [Fact]
    public void Boxes_survive_saving_and_loading()
    {
        var p = SampleProject.Create();
        var copy = ProjectIO.FromJson(ProjectIO.ToJson(p));
        Assert.Equal(p.FindType("Player")!.Box, copy.FindType("Player")!.Box);
        Assert.True(copy.FindType("Player")!.HasCustomBox);
    }

    [Fact]
    public void Smaller_spike_box_lets_the_player_stand_closer()
    {
        var spikes = BuiltInObjects.Find("Spikes")!;
        Assert.Equal(3, spikes.Box.Y);
    }
}
