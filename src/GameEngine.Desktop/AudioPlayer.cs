using System.Runtime.CompilerServices;
using System.Text.Json;
using GameEngine.Core;
using Silk.NET.OpenAL;

namespace GameEngine.Desktop;

/// <summary>
/// Plays sound effects and music through OpenAL. If no audio device is available the
/// player stays silent instead of failing, so games still run.
/// </summary>
public sealed unsafe class AudioPlayer : IDisposable
{
    const int EffectVoices = 24;

    static AudioPlayer? _shared;

    /// <summary>One player for the whole app (editor previews and play windows share it).</summary>
    public static AudioPlayer Shared => _shared ??= new AudioPlayer();

    readonly AL? _al;
    readonly ALContext? _alc;
    readonly Device* _device;
    readonly Context* _context;
    readonly Dictionary<string, uint> _buffers = new();
    readonly List<uint> _voices = new();
    readonly uint _music;
    string? _musicKey;
    int _nextVoice;
    bool _muted;

    public AudioPlayer()
    {
        try
        {
            _alc = ALContext.GetApi(soft: true);
            _al = AL.GetApi(soft: true);
            _device = _alc.OpenDevice("");
            if (_device == null) { Error = "No audio device was found."; return; }
            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);
            for (var i = 0; i < EffectVoices; i++) _voices.Add(_al.GenSource());
            _music = _al.GenSource();
            _al.SetSourceProperty(_music, SourceBoolean.Looping, true);
            Available = true;
        }
        catch (Exception ex)
        {
            // missing native library, sandbox without audio, CI machine…
            Error = ex.Message;
            Available = false;
        }
    }

    public bool Available { get; }
    public string? Error { get; private set; }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (Available) _al!.SetListenerProperty(ListenerFloat.Gain, value ? 0f : 1f);
        }
    }

    // ------------------------------------------------------------------ playing

    public void Play(SoundAsset sound, double volume = 1, double pitch = 1)
    {
        if (!Available || Buffer(sound) is not { } buffer) return;
        var voice = FreeVoice();
        _al!.SourceStop(voice);
        _al.SetSourceProperty(voice, SourceInteger.Buffer, (int)buffer);
        _al.SetSourceProperty(voice, SourceFloat.Gain, (float)Math.Clamp(sound.Volume * volume, 0, 2));
        _al.SetSourceProperty(voice, SourceFloat.Pitch, (float)Math.Clamp(pitch, 0.1, 4));
        _al.SourcePlay(voice);
    }

    public void PlayMusic(SoundAsset sound, double volume = 1)
    {
        if (!Available) return;
        var key = Key(sound);
        _al!.SetSourceProperty(_music, SourceFloat.Gain, (float)Math.Clamp(sound.Volume * volume, 0, 2));
        // the same track carries on rather than starting over
        if (key == _musicKey && IsPlaying(_music)) return;
        if (Buffer(sound) is not { } buffer) return;
        _al.SourceStop(_music);
        _al.SetSourceProperty(_music, SourceInteger.Buffer, (int)buffer);
        _al.SourcePlay(_music);
        _musicKey = key;
    }

    public void StopMusic()
    {
        if (!Available) return;
        _al!.SourceStop(_music);
        _musicKey = null;
    }

    public void SetMusicVolume(double volume)
    {
        if (Available) _al!.SetSourceProperty(_music, SourceFloat.Gain, (float)Math.Clamp(volume, 0, 2));
    }

    public void StopAll()
    {
        if (!Available) return;
        foreach (var v in _voices) _al!.SourceStop(v);
        StopMusic();
    }

    public bool MusicPlaying => Available && IsPlaying(_music);

    /// <summary>Carries out what a running game asked for.</summary>
    public void Process(Project project, List<AudioCommand> commands)
    {
        foreach (var c in commands)
        {
            switch (c)
            {
                case PlaySoundCommand p when project.FindSound(p.Sound) is { } s:
                    Play(s, p.Volume, p.Pitch);
                    break;
                case PlayMusicCommand m when project.FindSound(m.Sound) is { } s:
                    PlayMusic(s, m.Volume);
                    break;
                case StopMusicCommand:
                    StopMusic();
                    break;
                case MusicVolumeCommand v:
                    SetMusicVolume(v.Volume);
                    break;
            }
        }
        commands.Clear();
    }

    // ------------------------------------------------------------------ buffers

    bool IsPlaying(uint source)
    {
        _al!.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
        return state == (int)SourceState.Playing;
    }

    uint FreeVoice()
    {
        for (var i = 0; i < _voices.Count; i++)
        {
            var v = _voices[(_nextVoice + i) % _voices.Count];
            if (!IsPlaying(v)) { _nextVoice = (_nextVoice + i + 1) % _voices.Count; return v; }
        }
        // all busy: steal the oldest
        var stolen = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        return stolen;
    }

    /// <summary>Identifies a sound's audio, so edited sounds get a fresh buffer.</summary>
    static string Key(SoundAsset s) => s.IsSynth
        ? "synth:" + JsonSerializer.Serialize(s.Synth)
        : $"file:{s.FileType}:{RuntimeHelpers.GetHashCode(s.Data)}:{s.Data?.Length}";

    uint? Buffer(SoundAsset sound)
    {
        var key = Key(sound);
        if (_buffers.TryGetValue(key, out var existing)) return existing;
        PcmAudio pcm;
        try { pcm = AudioCodec.Decode(sound); }
        catch (Exception ex) { Error = $"Could not play {sound.Name}: {ex.Message}"; return null; }
        if (pcm.Samples.Length == 0) return null;

        // 16-bit mono or stereo (extra channels are dropped)
        var channels = Math.Min(2, pcm.Channels);
        var frames = pcm.Samples.Length / pcm.Channels;
        var data = new short[frames * channels];
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < channels; c++)
                data[f * channels + c] = (short)Math.Clamp(pcm.Samples[f * pcm.Channels + c] * 32767f, -32768f, 32767f);

        var buffer = _al!.GenBuffer();
        fixed (short* p = data)
            _al.BufferData(buffer, channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16, p, data.Length * 2, pcm.SampleRate);

        // keep memory in check while sounds are being tweaked
        if (_buffers.Count > 200) ClearUnused();
        _buffers[key] = buffer;
        return buffer;
    }

    void ClearUnused()
    {
        foreach (var v in _voices) _al!.SourceStop(v);
        foreach (var (key, buffer) in _buffers.ToList())
        {
            if (key == _musicKey) continue;
            _al!.DeleteBuffer(buffer);
            _buffers.Remove(key);
        }
    }

    public void Dispose()
    {
        if (!Available) return;
        StopAll();
        foreach (var v in _voices) _al!.DeleteSource(v);
        _al!.DeleteSource(_music);
        foreach (var b in _buffers.Values) _al.DeleteBuffer(b);
        _alc!.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
    }
}
