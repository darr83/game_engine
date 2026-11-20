using GameEngine.Core;

namespace GameEngine.Core.Tests;

public class SoundTests
{
    [Fact]
    public void Every_preset_renders_audible_sound_in_range()
    {
        foreach (var name in Synth.PresetNames)
        {
            var pcm = Synth.Render(Synth.Preset(name));
            Assert.True(pcm.Seconds > 0.02, name);
            Assert.All(pcm.Samples, s => Assert.InRange(s, -1f, 1f));
            Assert.True(pcm.Samples.Max(Math.Abs) > 0.2f, $"{name} is too quiet");
        }
    }

    [Fact]
    public void Random_sounds_are_valid()
    {
        for (var seed = 1; seed < 40; seed++)
            Assert.All(Synth.Render(Synth.Random(seed)).Samples, s => Assert.InRange(s, -1f, 1f));
    }

    [Fact]
    public void Wav_round_trips()
    {
        var pcm = new PcmAudio(new[] { 0f, 0.5f, -0.5f, 0.25f }, 2, 22050);
        var back = AudioCodec.DecodeWav(AudioCodec.EncodeWav(pcm));
        Assert.Equal(2, back.Channels);
        Assert.Equal(22050, back.SampleRate);
        Assert.Equal(pcm.Samples.Length, back.Samples.Length);
        for (var i = 0; i < pcm.Samples.Length; i++) Assert.Equal(pcm.Samples[i], back.Samples[i], 3);
    }

    [Fact]
    public void Sample_music_decodes()
    {
        var music = SampleProject.Create().FindSound("Theme")!;
        var pcm = AudioCodec.Decode(music);
        Assert.InRange(pcm.Seconds, 5, 20);
    }

    [Fact]
    public void Sounds_survive_saving_and_loading()
    {
        var p = SampleProject.Create();
        var copy = ProjectIO.FromJson(ProjectIO.ToJson(p));
        Assert.Equal(p.Sounds.Count, copy.Sounds.Count);
        Assert.Equal(p.FindSound("Theme")!.Data, copy.FindSound("Theme")!.Data);
        Assert.Equal(p.FindSound("Coin")!.Synth.StartFrequency, copy.FindSound("Coin")!.Synth.StartFrequency);
        Assert.Equal("Theme", copy.Levels[0].Music);
    }

    [Fact]
    public void Level_music_starts_when_the_level_loads_and_not_again_on_restart()
    {
        var p = SampleProject.Create();
        var w = new World(p);
        Assert.Contains(w.Audio, c => c is PlayMusicCommand { Sound: "Theme" });
        w.Audio.Clear();
        w.Entities.First(e => e.Type == "Player").Y = 2000;   // fall out → restart
        for (var i = 0; i < 3; i++) w.Step();
        Assert.Equal(1, w.Restarts);
        Assert.DoesNotContain(w.Audio, c => c is PlayMusicCommand);
        Assert.Contains(w.Audio, c => c is PlaySoundCommand { Sound: "Hurt" });
    }

    [Fact]
    public void Jumping_plays_the_jump_sound_once()
    {
        var p = SampleProject.CreateBlank();
        var w = new World(p);
        for (var i = 0; i < 30; i++) w.Step();
        w.Audio.Clear();
        w.Input.KeyDown("Space");
        for (var i = 0; i < 10; i++) w.Step();
        Assert.Single(w.Audio, c => c is PlaySoundCommand { Sound: "Jump" });
    }

    [Fact]
    public void Landing_is_detected()
    {
        var p = SampleProject.CreateBlank();
        p.Events.Add(new GameEvent { Conditions = { new EventItem("Lands", ("A", "Player")) }, Actions = { new EventItem("AddVar", ("Var", "Score"), ("Value", "1")) } });
        var w = new World(p);
        for (var i = 0; i < 30; i++) w.Step();
        var before = w.Vars["Score"];
        w.Input.KeyDown("Space");
        w.Step();
        w.Input.KeyUp("Space");
        for (var i = 0; i < 90; i++) w.Step();
        Assert.Equal(before + 1, w.Vars["Score"]);
    }
}
