using System.Text;

namespace GameEngine.Core;

public enum Waveform { Square, Triangle, Sine, Saw, Noise }

/// <summary>
/// Settings for a generated retro sound effect: a tone that slides in pitch, shaped by a
/// volume envelope, with optional vibrato and a pitch jump part-way through.
/// </summary>
public sealed class SynthSettings
{
    public Waveform Wave { get; set; } = Waveform.Square;
    /// <summary>Starting pitch in Hz.</summary>
    public double StartFrequency { get; set; } = 440;
    /// <summary>Ending pitch in Hz; different from the start makes the pitch slide.</summary>
    public double EndFrequency { get; set; } = 440;
    /// <summary>Seconds to fade in.</summary>
    public double Attack { get; set; } = 0.0;
    /// <summary>Seconds at full volume.</summary>
    public double Sustain { get; set; } = 0.1;
    /// <summary>Seconds to fade out.</summary>
    public double Decay { get; set; } = 0.15;
    /// <summary>For square waves: 0.5 is a hollow tone, lower is thinner and buzzier.</summary>
    public double Duty { get; set; } = 0.5;
    public double VibratoDepth { get; set; }
    public double VibratoSpeed { get; set; } = 8;
    /// <summary>Pitch multiplier applied after <see cref="JumpTime"/>; 1 means no jump.</summary>
    public double JumpMultiplier { get; set; } = 1;
    public double JumpTime { get; set; } = 0.1;

    public SynthSettings Clone() => (SynthSettings)MemberwiseClone();

    public double Duration => Attack + Sustain + Decay;
}

/// <summary>A sound effect or music track stored in the project.</summary>
public sealed class SoundAsset
{
    public string Name { get; set; } = "Sound";

    /// <summary>"synth" for generated effects, "file" for imported audio.</summary>
    public string Source { get; set; } = "synth";

    public SynthSettings Synth { get; set; } = new();

    /// <summary>For imported audio: "wav", "ogg" or "mp3".</summary>
    public string FileType { get; set; } = "";

    /// <summary>For imported audio: the original file's bytes (saved as base64).</summary>
    public byte[]? Data { get; set; }

    /// <summary>0 to 1.</summary>
    public double Volume { get; set; } = 0.8;

    public bool IsSynth => Source == "synth";

    public SoundAsset Clone() => new()
    {
        Name = Name, Source = Source, Synth = Synth.Clone(), FileType = FileType,
        Data = Data, Volume = Volume,
    };
}

/// <summary>Decoded audio: interleaved samples between -1 and 1.</summary>
public sealed record PcmAudio(float[] Samples, int Channels, int SampleRate)
{
    public double Seconds => Samples.Length / (double)Channels / SampleRate;
}

/// <summary>Something the running game asks the audio system to do.</summary>
public abstract record AudioCommand;
public sealed record PlaySoundCommand(string Sound, double Volume, double Pitch) : AudioCommand;
public sealed record PlayMusicCommand(string Sound, double Volume) : AudioCommand;
public sealed record StopMusicCommand : AudioCommand;
public sealed record MusicVolumeCommand(double Volume) : AudioCommand;

/// <summary>Generates retro sound effects.</summary>
public static class Synth
{
    public const int SampleRate = 44100;

    public static PcmAudio Render(SynthSettings s)
    {
        var total = Math.Clamp(s.Duration, 0.01, 10);
        var count = (int)(total * SampleRate);
        var samples = new float[count];
        var phase = 0.0;
        var noise = new Random(7);
        var noiseValue = 0.0;
        var lastCycle = -1L;

        for (var i = 0; i < count; i++)
        {
            var t = i / (double)SampleRate;
            var progress = t / total;

            // pitch: exponential slide, optional jump, vibrato
            var freq = s.StartFrequency * Math.Pow(Math.Max(1, s.EndFrequency) / Math.Max(1, s.StartFrequency), progress);
            if (s.JumpMultiplier != 1 && t >= s.JumpTime) freq *= s.JumpMultiplier;
            if (s.VibratoDepth > 0) freq *= 1 + Math.Sin(t * s.VibratoSpeed * 2 * Math.PI) * s.VibratoDepth * 0.1;
            freq = Math.Clamp(freq, 20, 20000);

            phase += freq / SampleRate;
            var p = phase - Math.Floor(phase);
            double v = s.Wave switch
            {
                Waveform.Square => p < Math.Clamp(s.Duty, 0.05, 0.95) ? 0.6 : -0.6,
                Waveform.Triangle => (p < 0.5 ? 4 * p - 1 : 3 - 4 * p) * 0.9,
                Waveform.Sine => Math.Sin(p * 2 * Math.PI),
                Waveform.Saw => (2 * p - 1) * 0.6,
                Waveform.Noise => NoiseSample(),
                _ => 0,
            };

            double NoiseSample()
            {
                // new random value every half cycle, so pitch still changes the colour of the noise
                var cycle = (long)(phase * 2);
                if (cycle != lastCycle) { noiseValue = noise.NextDouble() * 2 - 1; lastCycle = cycle; }
                return noiseValue * 0.7;
            }

            samples[i] = (float)(v * Envelope(s, t));
        }
        return new PcmAudio(samples, 1, SampleRate);
    }

    static double Envelope(SynthSettings s, double t)
    {
        if (t < s.Attack) return s.Attack <= 0 ? 1 : t / s.Attack;
        t -= s.Attack;
        if (t < s.Sustain) return 1;
        t -= s.Sustain;
        return s.Decay <= 0 ? 0 : Math.Max(0, 1 - t / s.Decay);
    }

    public static readonly string[] PresetNames = { "Coin", "Jump", "Hit", "Explosion", "Power-up", "Laser", "Blip" };

    /// <summary>A sound in the style of a preset. Different seeds give variations.</summary>
    public static SynthSettings Preset(string name, int seed = 0)
    {
        var r = new Random(seed);
        double Vary(double v, double amount) => seed == 0 ? v : v * (1 + (r.NextDouble() * 2 - 1) * amount);
        return name switch
        {
            "Coin" => new SynthSettings
            {
                Wave = Waveform.Square, Duty = 0.5, StartFrequency = Vary(990, 0.2), EndFrequency = Vary(990, 0.2),
                Sustain = Vary(0.05, 0.3), Decay = Vary(0.25, 0.3), JumpMultiplier = 1.335, JumpTime = Vary(0.06, 0.3),
            },
            "Jump" => new SynthSettings
            {
                Wave = Waveform.Square, Duty = 0.25, StartFrequency = Vary(260, 0.25), EndFrequency = Vary(620, 0.25),
                Sustain = Vary(0.08, 0.3), Decay = Vary(0.12, 0.3),
            },
            "Hit" => new SynthSettings
            {
                Wave = Waveform.Noise, StartFrequency = Vary(900, 0.3), EndFrequency = Vary(120, 0.3),
                Sustain = 0.02, Decay = Vary(0.18, 0.3),
            },
            "Explosion" => new SynthSettings
            {
                Wave = Waveform.Noise, StartFrequency = Vary(320, 0.3), EndFrequency = Vary(40, 0.3),
                Sustain = Vary(0.1, 0.3), Decay = Vary(0.6, 0.3),
            },
            "Power-up" => new SynthSettings
            {
                Wave = Waveform.Square, Duty = 0.4, StartFrequency = Vary(300, 0.2), EndFrequency = Vary(900, 0.2),
                Sustain = Vary(0.25, 0.3), Decay = 0.2, VibratoDepth = Vary(0.6, 0.5), VibratoSpeed = Vary(18, 0.3),
            },
            "Laser" => new SynthSettings
            {
                Wave = Waveform.Saw, StartFrequency = Vary(1400, 0.25), EndFrequency = Vary(180, 0.3),
                Sustain = Vary(0.05, 0.3), Decay = Vary(0.15, 0.3),
            },
            _ => new SynthSettings
            {
                Wave = Waveform.Square, Duty = 0.5, StartFrequency = Vary(660, 0.3), EndFrequency = Vary(660, 0.3),
                Sustain = 0.03, Decay = 0.05,
            },
        };
    }

    /// <summary>A completely random sound.</summary>
    public static SynthSettings Random(int seed)
    {
        var r = new Random(seed);
        var start = 100 + r.NextDouble() * 1400;
        return new SynthSettings
        {
            Wave = (Waveform)r.Next(5),
            StartFrequency = start,
            EndFrequency = r.NextDouble() < 0.4 ? start : 60 + r.NextDouble() * 1600,
            Attack = r.NextDouble() < 0.7 ? 0 : r.NextDouble() * 0.15,
            Sustain = r.NextDouble() * 0.3,
            Decay = 0.05 + r.NextDouble() * 0.5,
            Duty = 0.1 + r.NextDouble() * 0.4,
            VibratoDepth = r.NextDouble() < 0.7 ? 0 : r.NextDouble(),
            VibratoSpeed = 2 + r.NextDouble() * 20,
            JumpMultiplier = r.NextDouble() < 0.7 ? 1 : 0.5 + r.NextDouble() * 1.5,
            JumpTime = r.NextDouble() * 0.2,
        };
    }
}

/// <summary>Reads imported audio files and writes WAV.</summary>
public static class AudioCodec
{
    public static readonly string[] Extensions = { "wav", "ogg", "mp3" };

    public static PcmAudio Decode(SoundAsset sound)
    {
        if (sound.IsSynth) return Synth.Render(sound.Synth);
        if (sound.Data is null || sound.Data.Length == 0) return new PcmAudio(Array.Empty<float>(), 1, Synth.SampleRate);
        return sound.FileType switch
        {
            "wav" => DecodeWav(sound.Data),
            "ogg" => DecodeOgg(sound.Data),
            "mp3" => DecodeMp3(sound.Data),
            _ => throw new NotSupportedException($"Unsupported audio type: {sound.FileType}"),
        };
    }

    public static PcmAudio DecodeWav(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("not a WAV file.");
        reader.ReadInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("not a WAV file.");

        int format = 0, channels = 0, rate = 0, bits = 0;
        byte[]? pcm = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            var next = reader.BaseStream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                if (format == 0xFFFE && size >= 26)
                {
                    reader.ReadInt16(); reader.ReadInt16(); reader.ReadInt32();
                    format = reader.ReadInt16();   // sub-format: 1 = PCM, 3 = float
                }
            }
            else if (id == "data")
            {
                pcm = reader.ReadBytes((int)Math.Min(size, reader.BaseStream.Length - reader.BaseStream.Position));
            }
            if (next > reader.BaseStream.Length) break;
            reader.BaseStream.Position = next;
        }
        if (pcm is null || channels == 0) throw new InvalidDataException("the WAV file has no audio data.");

        var bytesPer = bits / 8;
        var count = pcm.Length / bytesPer;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var o = i * bytesPer;
            samples[i] = (format, bits) switch
            {
                (1, 8) => (pcm[o] - 128) / 128f,
                (1, 16) => BitConverter.ToInt16(pcm, o) / 32768f,
                (1, 24) => ((pcm[o] | pcm[o + 1] << 8 | (sbyte)pcm[o + 2] << 16)) / 8388608f,
                (1, 32) => BitConverter.ToInt32(pcm, o) / 2147483648f,
                (3, 32) => BitConverter.ToSingle(pcm, o),
                _ => throw new NotSupportedException($"WAV format {format} with {bits} bits is not supported."),
            };
        }
        return new PcmAudio(samples, channels, rate);
    }

    static PcmAudio DecodeOgg(byte[] data)
    {
        using var vorbis = new NVorbis.VorbisReader(new MemoryStream(data), closeOnDispose: true);
        var all = new List<float>();
        var buffer = new float[vorbis.Channels * 4096];
        int read;
        while ((read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer.AsSpan(0, read).ToArray());
        return new PcmAudio(all.ToArray(), vorbis.Channels, vorbis.SampleRate);
    }

    static PcmAudio DecodeMp3(byte[] data)
    {
        var mp3 = new NLayer.MpegFile(new MemoryStream(data));
        var all = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = mp3.ReadSamples(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer.AsSpan(0, read).ToArray());
        return new PcmAudio(all.ToArray(), mp3.Channels, mp3.SampleRate);
    }

    /// <summary>16-bit PCM WAV.</summary>
    public static byte[] EncodeWav(PcmAudio audio)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataSize = audio.Samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataSize);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)audio.Channels);
        w.Write(audio.SampleRate);
        w.Write(audio.SampleRate * audio.Channels * 2);
        w.Write((short)(audio.Channels * 2));
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataSize);
        foreach (var s in audio.Samples) w.Write((short)Math.Clamp(s * 32767f, -32768f, 32767f));
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>Writes a short chiptune loop for the sample project.</summary>
public static class SampleMusic
{
    public static byte[] RenderWav()
    {
        const int rate = 22050;
        const double bpm = 132;
        var step = 60.0 / bpm / 2;            // eighth notes
        // C major-ish melody and bass, 32 steps (4 bars); 0 = rest
        int[] melody = { 72, 0, 76, 79, 76, 0, 72, 74, 76, 0, 74, 72, 69, 0, 67, 0,
                         72, 0, 76, 79, 81, 79, 76, 0, 74, 76, 74, 71, 72, 0, 0, 0 };
        int[] bass = { 48, 48, 55, 55, 45, 45, 52, 52, 41, 41, 48, 48, 43, 43, 50, 50,
                       48, 48, 55, 55, 45, 45, 52, 52, 41, 41, 43, 43, 48, 48, 48, 48 };
        var length = (int)(step * melody.Length * rate);
        var samples = new float[length];

        void Note(int midi, int index, double volume, double duty, bool triangle)
        {
            if (midi == 0) return;
            var freq = 440 * Math.Pow(2, (midi - 69) / 12.0);
            var start = (int)(index * step * rate);
            var dur = (int)(step * rate * 0.9);
            for (var i = 0; i < dur && start + i < length; i++)
            {
                var p = i * freq / rate % 1;
                var v = triangle ? (p < 0.5 ? 4 * p - 1 : 3 - 4 * p) : (p < duty ? 1 : -1);
                var env = Math.Min(1, (dur - i) / (rate * 0.02));
                samples[start + i] += (float)(v * volume * env);
            }
        }

        for (var i = 0; i < melody.Length; i++)
        {
            Note(melody[i], i, 0.16, 0.25, false);
            Note(bass[i], i, 0.22, 0.5, true);
        }
        return AudioCodec.EncodeWav(new PcmAudio(samples, 1, rate));
    }
}
