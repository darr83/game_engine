using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

/// <summary>Draws a sound's waveform.</summary>
public sealed class WaveformView : Control
{
    float[] _peaks = Array.Empty<float>();
    public IBrush Brush { get; set; } = UiTheme.Accent;

    public void Show(PcmAudio pcm)
    {
        // one peak per pixel column, worked out at a fixed resolution
        const int columns = 600;
        _peaks = new float[columns];
        var frames = pcm.Samples.Length / Math.Max(1, pcm.Channels);
        if (frames > 0)
            for (var c = 0; c < columns; c++)
            {
                int from = (int)((long)c * frames / columns), to = Math.Max(from + 1, (int)((long)(c + 1) * frames / columns));
                var peak = 0f;
                for (var f = from; f < to && f < frames; f++) peak = Math.Max(peak, Math.Abs(pcm.Samples[f * pcm.Channels]));
                _peaks[c] = peak;
            }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        ctx.DrawRectangle(UiTheme.Background, null, r, 8, 8);
        var mid = r.Height / 2;
        ctx.DrawLine(new Pen(UiTheme.Border, 1), new Point(0, mid), new Point(r.Width, mid));
        if (_peaks.Length == 0) return;
        var pen = new Pen(Brush, Math.Max(1, r.Width / _peaks.Length));
        for (var x = 0; x < r.Width; x += 2)
        {
            var p = _peaks[(int)(x / r.Width * _peaks.Length)] * (mid - 6);
            ctx.DrawLine(pen, new Point(x, mid - p), new Point(x, mid + p));
        }
    }
}

/// <summary>
/// The Sounds page: the project's sound effects and music. Effects can be generated from
/// presets and tuned with sliders, or imported from WAV, OGG and MP3 files.
/// </summary>
public sealed class SoundsView : UserControl
{
    readonly StackPanel _list = new() { Spacing = 4 };
    readonly StackPanel _detail = new() { Spacing = 14, Margin = new Thickness(28, 22, 28, 32), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
    readonly WaveformView _wave = new() { Height = 96 };
    readonly TextBlock _message = new() { FontSize = 12, Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _autoPlay;
    Project _project = new();
    int _selected;
    int _seed = 1;
    DateTime _lastSnapshot;
    bool _playOnChange = true;

    public SoundsView()
    {
        _autoPlay = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Background, (_, _) =>
        {
            _autoPlay!.Stop();
            if (Current is { } s) AudioPlayer.Shared.Play(s);
        });

        var listPanel = new StackPanel { Spacing = 8, Margin = new Thickness(14, 16, 14, 16) };
        listPanel.Children.Add(Ui.Label("Sounds", 15, weight: FontWeight.SemiBold));
        listPanel.Children.Add(Ui.Label("Effects and music for your game. Play them from the event sheet.", 12, UiTheme.Muted));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        buttons.Children.Add(Ui.IconButton(Ui.Icons.Plus, "Make a new sound effect", NewEffect, "New effect"));
        buttons.Children.Add(Ui.IconButton(Ui.Icons.Open, "Import a WAV, OGG or MP3 file", () => _ = Import(), "Import"));
        listPanel.Children.Add(buttons);
        listPanel.Children.Add(_list);

        var left = new Border
        {
            Width = 280, Background = UiTheme.Panel, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Content = listPanel },
        };
        var dock = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        dock.Children.Add(left);
        dock.Children.Add(new ScrollViewer { Content = _detail });
        Content = dock;
    }

    public event Action? Changing;
    public event Action? Changed;

    SoundAsset? Current => _selected >= 0 && _selected < _project.Sounds.Count ? _project.Sounds[_selected] : null;

    public void Show(Project project)
    {
        _project = project;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, project.Sounds.Count - 1));
        RebuildList();
        RebuildDetail();
    }

    /// <summary>Saves an undo step, at most once per burst of slider movement.</summary>
    void Snapshot(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _lastSnapshot).TotalMilliseconds < 600) { _lastSnapshot = DateTime.UtcNow; return; }
        _lastSnapshot = DateTime.UtcNow;
        Changing?.Invoke();
    }

    void Edited(bool rebuildList = false)
    {
        Changed?.Invoke();
        if (rebuildList) RebuildList();
    }

    // ------------------------------------------------------------------ list

    void RebuildList()
    {
        _list.Children.Clear();
        if (_project.Sounds.Count == 0)
            _list.Children.Add(Ui.Label("No sounds yet.", 12, UiTheme.Muted));

        for (var i = 0; i < _project.Sounds.Count; i++)
        {
            var index = i;
            var s = _project.Sounds[i];
            var play = Ui.IconButton(Ui.Icons.Play, $"Play {s.Name}", () => PlayPreview(s), iconBrush: UiTheme.Play);
            play.Padding = new Thickness(6);

            var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = Summary(s), FontSize = 11, Foreground = UiTheme.Muted });

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(play);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var border = new Border
            {
                Child = row, Padding = new Thickness(4, 4, 8, 4), CornerRadius = new CornerRadius(8),
                Background = i == _selected ? UiTheme.AccentSoft : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            border.PointerPressed += (_, e) =>
            {
                if (e.Source is Visual v && IsInside(v, play)) return;
                _selected = index;
                RebuildList();
                RebuildDetail();
            };
            _list.Children.Add(border);
        }
    }

    static bool IsInside(Visual v, Visual container)
    {
        for (Visual? x = v; x is not null; x = Avalonia.VisualTree.VisualExtensions.GetVisualParent(x))
            if (x == container) return true;
        return false;
    }

    static string Summary(SoundAsset s)
    {
        if (s.IsSynth) return $"Effect · {s.Synth.Duration:0.00}s";
        try
        {
            var seconds = AudioCodec.Decode(s).Seconds;
            return $"{s.FileType.ToUpperInvariant()} · {(int)seconds / 60}:{(int)seconds % 60:00}";
        }
        catch { return $"{s.FileType.ToUpperInvariant()} · can't be read"; }
    }

    void PlayPreview(SoundAsset s)
    {
        if (!AudioPlayer.Shared.Available)
        {
            _message.Text = "Sound can't play on this computer: " + (AudioPlayer.Shared.Error ?? "no audio device.");
            return;
        }
        if (s.IsSynth) AudioPlayer.Shared.Play(s);
        else AudioPlayer.Shared.PlayMusic(s);
    }

    // ------------------------------------------------------------------ detail

    void RebuildDetail()
    {
        _detail.Children.Clear();
        _message.Text = "";
        if (Current is not { } s)
        {
            _detail.Children.Add(Ui.Label("Make a sound effect with New effect, or import an audio file.", 13, UiTheme.Muted));
            return;
        }

        // name, volume, actions
        var name = new TextBox { Text = s.Name, Width = 220, FontSize = 15, FontWeight = FontWeight.SemiBold };
        name.LostFocus += (_, _) => Rename(s, (name.Text ?? "").Trim());
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(name);
        header.Children.Add(Ui.Primary(Ui.Icons.Play, "Play", UiTheme.Play, () => PlayPreview(s)));
        header.Children.Add(Ui.IconButton(Ui.Icons.Stop, "Stop all sounds", () => AudioPlayer.Shared.StopAll(), "Stop"));
        header.Children.Add(Ui.Divider(Orientation.Vertical));
        header.Children.Add(Ui.IconButton(Ui.Icons.Copy, "Duplicate this sound", () => Duplicate(s)));
        header.Children.Add(Ui.IconButton(Ui.Icons.Trash, "Delete this sound", () => Delete(s), iconBrush: UiTheme.Danger));
        _detail.Children.Add(header);
        _detail.Children.Add(_message);

        _detail.Children.Add(SliderRow("Volume", s.Volume * 100, 0, 100, "0", v => s.Volume = v / 100));
        _wave.Brush = s.IsSynth ? UiTheme.Accent : UiTheme.Action;
        _detail.Children.Add(_wave);
        RefreshWave();

        if (s.IsSynth) BuildSynth(s);
        else BuildFileInfo(s);
    }

    void RefreshWave()
    {
        if (Current is not { } s) return;
        try { _wave.Show(AudioCodec.Decode(s)); }
        catch (Exception ex) { _message.Text = ex.Message; }
    }

    void BuildSynth(SoundAsset s)
    {
        var syn = s.Synth;

        _detail.Children.Add(Ui.Heading("Start from a preset"));
        _detail.Children.Add(Ui.Label("Each click makes a new variation.", 12, UiTheme.Muted));
        var presets = new WrapPanel();
        foreach (var preset in Synth.PresetNames)
        {
            var p = preset;
            var b = new Button { Content = p, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 6) };
            b.Click += (_, _) => ApplySynth(s, Synth.Preset(p, _seed++));
            presets.Children.Add(b);
        }
        var random = new Button { Content = "Surprise me", Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 6), Foreground = UiTheme.Accent };
        random.Click += (_, _) => ApplySynth(s, Synth.Random(Environment.TickCount + _seed++));
        presets.Children.Add(random);
        _detail.Children.Add(presets);

        var auto = new CheckBox { Content = "Play while adjusting", IsChecked = _playOnChange };
        auto.IsCheckedChanged += (_, _) => _playOnChange = auto.IsChecked == true;
        _detail.Children.Add(auto);

        _detail.Children.Add(Ui.Heading("Tone"));
        var waves = Enum.GetValues<Waveform>();
        _detail.Children.Add(Ui.Segmented(waves.Select(w => w.ToString()).ToArray(), Array.IndexOf(waves, syn.Wave), i =>
        {
            Snapshot(force: true);
            syn.Wave = waves[i];
            SynthChanged();
        }, out _));

        var grid = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 28 };
        var leftCol = new StackPanel { Spacing = 10 };
        var rightCol = new StackPanel { Spacing = 10 };
        Grid.SetColumn(rightCol, 1);
        grid.Children.Add(leftCol);
        grid.Children.Add(rightCol);

        leftCol.Children.Add(Ui.Heading("Pitch"));
        leftCol.Children.Add(SliderRow("Start pitch (Hz)", syn.StartFrequency, 40, 3000, "0", v => syn.StartFrequency = v, SynthChanged));
        leftCol.Children.Add(SliderRow("End pitch (Hz)", syn.EndFrequency, 40, 3000, "0", v => syn.EndFrequency = v, SynthChanged));
        leftCol.Children.Add(SliderRow("Pitch jump (×)", syn.JumpMultiplier, 0.5, 2, "0.00", v => syn.JumpMultiplier = v, SynthChanged));
        leftCol.Children.Add(SliderRow("Jump after (s)", syn.JumpTime, 0, 0.5, "0.00", v => syn.JumpTime = v, SynthChanged));
        leftCol.Children.Add(SliderRow("Vibrato depth", syn.VibratoDepth, 0, 1, "0.00", v => syn.VibratoDepth = v, SynthChanged));
        leftCol.Children.Add(SliderRow("Vibrato speed", syn.VibratoSpeed, 0, 30, "0.0", v => syn.VibratoSpeed = v, SynthChanged));

        rightCol.Children.Add(Ui.Heading("Volume shape"));
        rightCol.Children.Add(SliderRow("Fade in (s)", syn.Attack, 0, 1, "0.00", v => syn.Attack = v, SynthChanged));
        rightCol.Children.Add(SliderRow("Hold (s)", syn.Sustain, 0, 1.5, "0.00", v => syn.Sustain = v, SynthChanged));
        rightCol.Children.Add(SliderRow("Fade out (s)", syn.Decay, 0, 2, "0.00", v => syn.Decay = v, SynthChanged));
        rightCol.Children.Add(SliderRow("Square width", syn.Duty, 0.05, 0.95, "0.00", v => syn.Duty = v, SynthChanged));
        rightCol.Children.Add(Ui.Label("Square width only changes square waves: 0.5 is hollow, lower is thin and buzzy.", 11, UiTheme.Muted));
        _detail.Children.Add(grid);
    }

    void ApplySynth(SoundAsset s, SynthSettings settings)
    {
        Snapshot(force: true);
        s.Synth = settings;
        RebuildDetail();
        RebuildList();
        Edited();
        AudioPlayer.Shared.Play(s);
    }

    void SynthChanged()
    {
        RefreshWave();
        RebuildListSoon();
        if (_playOnChange) { _autoPlay.Stop(); _autoPlay.Start(); }
    }

    void RebuildListSoon() => Dispatcher.UIThread.Post(RebuildList, DispatcherPriority.Background);

    void BuildFileInfo(SoundAsset s)
    {
        _detail.Children.Add(Ui.Heading("Audio file"));
        try
        {
            var pcm = AudioCodec.Decode(s);
            var seconds = pcm.Seconds;
            _detail.Children.Add(Ui.Label(
                $"{s.FileType.ToUpperInvariant()} · {(int)seconds / 60}:{(int)seconds % 60:00} · {(pcm.Channels == 1 ? "mono" : "stereo")} · {pcm.SampleRate:N0} Hz · {s.Data?.Length / 1024:N0} KB",
                13));
        }
        catch (Exception ex)
        {
            _detail.Children.Add(Ui.Label("This file can't be read: " + ex.Message, 13, UiTheme.Danger));
        }
        _detail.Children.Add(Ui.Label("Use it as a level's music in the level settings (the right panel when nothing is selected), or play it with “Play sound” or “Play music” in the event sheet. Previews of files play as music; Stop ends them.", 12, UiTheme.Muted));
    }

    Control SliderRow(string label, double value, double min, double max, string format, Action<double> set, Action? after = null)
    {
        var readout = new TextBlock { Text = value.ToString(format, CultureInfo.InvariantCulture), Foreground = UiTheme.Muted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Margin = new Thickness(0, -6, 0, -6) };
        slider.ValueChanged += (_, e) =>
        {
            Snapshot();
            set(e.NewValue);
            readout.Text = e.NewValue.ToString(format, CultureInfo.InvariantCulture);
            Edited();
            after?.Invoke();
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(Ui.Label(label, 12, UiTheme.Muted));
        Grid.SetColumn(readout, 1);
        top.Children.Add(readout);
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(slider);
        return stack;
    }

    // ------------------------------------------------------------------ commands

    void NewEffect()
    {
        Snapshot(force: true);
        var s = new SoundAsset { Name = _project.NewSoundName("Effect"), Synth = Synth.Preset("Blip", _seed++) };
        _project.Sounds.Add(s);
        _selected = _project.Sounds.Count - 1;
        Show(_project);
        Edited();
    }

    async Task Import()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import audio",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Audio (WAV, OGG, MP3)") { Patterns = new[] { "*.wav", "*.ogg", "*.mp3" } } },
        });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path) ImportFile(path);
    }

    /// <summary>Adds an audio file to the project. Returns the new sound, or null with a message shown.</summary>
    public SoundAsset? ImportFile(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!AudioCodec.Extensions.Contains(ext)) { _message.Text = $"{Path.GetFileName(path)} isn't a WAV, OGG or MP3 file."; return null; }
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 40 * 1024 * 1024) { _message.Text = $"{Path.GetFileName(path)} is over 40 MB. Try an OGG or MP3 version."; return null; }

        var sound = new SoundAsset
        {
            Name = _project.NewSoundName(Path.GetFileNameWithoutExtension(path)),
            Source = "file", FileType = ext, Data = bytes, Volume = 0.8,
        };
        try { AudioCodec.Decode(sound); }
        catch (Exception ex) { _message.Text = $"Could not read {Path.GetFileName(path)}: {ex.Message}"; return null; }

        Snapshot(force: true);
        _project.Sounds.Add(sound);
        _selected = _project.Sounds.Count - 1;
        Show(_project);
        _message.Text = $"Imported {Path.GetFileName(path)}.";
        Edited();
        return sound;
    }

    void Rename(SoundAsset s, string newName)
    {
        if (newName.Length == 0 || newName == s.Name || _project.FindSound(newName) is not null) { RebuildDetail(); return; }
        Snapshot(force: true);
        var old = s.Name;
        s.Name = newName;
        // keep events and level music pointing at it
        foreach (var item in _project.Events.SelectMany(e => e.Conditions.Concat(e.Actions)))
            if (item.Args.TryGetValue("Sound", out var v) && v == old) item.Args["Sound"] = newName;
        foreach (var level in _project.Levels.Where(l => l.Music == old)) level.Music = newName;
        Edited(rebuildList: true);
    }

    void Duplicate(SoundAsset s)
    {
        Snapshot(force: true);
        var copy = s.Clone();
        copy.Name = _project.NewSoundName(s.Name);
        _project.Sounds.Insert(_selected + 1, copy);
        _selected++;
        Show(_project);
        Edited();
    }

    void Delete(SoundAsset s)
    {
        Snapshot(force: true);
        _project.Sounds.Remove(s);
        foreach (var level in _project.Levels.Where(l => l.Music == s.Name)) level.Music = "";
        AudioPlayer.Shared.StopAll();
        Show(_project);
        _message.Text = $"Deleted {s.Name}. Ctrl+Z brings it back.";
        Edited();
    }
}
