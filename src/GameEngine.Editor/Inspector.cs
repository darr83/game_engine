using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

/// <summary>
/// Right-hand panel. Shows the selected object's settings, or the level's and game's
/// settings when nothing is selected.
/// </summary>
public sealed class Inspector : UserControl
{
    readonly StackPanel _stack = new() { Spacing = 12, Margin = new Thickness(16, 14, 16, 20) };
    Project _project = null!;
    Level _level = null!;
    Instance? _instance;

    public Inspector()
    {
        Content = new ScrollViewer { Content = _stack };
    }

    /// <summary>Raised before a value changes (for undo) and after (to redraw).</summary>
    public event Action? Changing;
    public event Action? Changed;
    public event Action? LevelsChanged;
    public event Action? DeleteRequested;
    public event Action<string>? EditTypeRequested;

    public void Show(Project project, Level level, Instance? instance)
    {
        _project = project;
        _level = level;
        _instance = instance;
        Rebuild();
    }

    /// <summary>Refreshes values without stealing focus from a field being edited.</summary>
    public void Refresh()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control c && IsAncestorOf(c)) return;
        Rebuild();
    }

    bool IsAncestorOf(Control c)
    {
        for (Visual? v = c; v is not null; v = v.GetVisualParent())
            if (v == this) return true;
        return false;
    }

    void Edit(Action change)
    {
        Changing?.Invoke();
        change();
        Changed?.Invoke();
    }

    void Rebuild()
    {
        _stack.Children.Clear();
        if (_instance is not null && _project.FindType(_instance.Type) is { } def) BuildObject(_instance, def);
        else BuildLevelAndGame();
    }

    void BuildObject(Instance inst, ObjectType def)
    {
        var header = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnGap = 12 };
        var scale = Math.Max(1, Math.Floor(36.0 / Math.Max(def.Width, def.Height)));
        var img = new Image { Source = SpriteCache.Get(def.Icon), Width = def.Width * scale, Height = def.Height * scale };
        RenderOptions.SetBitmapInterpolationMode(img, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        header.Children.Add(new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(8), Background = UiTheme.Surface, Child = img,
        });
        var title = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Ui.Label(def.Name, 15, weight: FontWeight.SemiBold));
        title.Children.Add(Ui.Label(def.Category, 12, UiTheme.Muted));
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        _stack.Children.Add(header);
        if (def.Description.Length > 0) _stack.Children.Add(Ui.Label(def.Description, 12, UiTheme.Muted));
        var editType = new Button
        {
            Content = $"Edit {def.Name}'s sprite and behaviour…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(editType, "Changes every copy of this object");
        editType.Click += (_, _) => EditTypeRequested?.Invoke(def.Id);
        _stack.Children.Add(editType);
        _stack.Children.Add(Ui.Divider());

        _stack.Children.Add(Ui.Heading("Position"));
        var pos = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 8 };
        var x = Ui.Field("X", Ui.Number(inst.X, -10000, 100000, 1, v => Edit(() => inst.X = v)));
        var y = Ui.Field("Y", Ui.Number(inst.Y, -10000, 100000, 1, v => Edit(() => inst.Y = v)));
        Grid.SetColumn(y, 1);
        pos.Children.Add(x);
        pos.Children.Add(y);
        _stack.Children.Add(pos);

        if (def.Properties.Length > 0)
        {
            _stack.Children.Add(Ui.Heading("This copy's settings"));
            foreach (var p in def.Properties)
            {
                var prop = p;
                _stack.Children.Add(Ui.Field(p.Label, Ui.Number(inst.Get(def, p.Name), p.Min, p.Max, p.Step,
                    v => Edit(() => inst.Props[prop.Name] = v))));
            }
        }

        _stack.Children.Add(Ui.Heading("Behaviours"));
        var chips = new WrapPanel();
        foreach (var info in BehaviourCatalog.All.Where(i => def.Has(i.Flag)))
            chips.Children.Add(Chip(info.Name));
        if (def.Behaviours == Behaviour.None) chips.Children.Add(Chip("Trigger (use events)"));
        _stack.Children.Add(chips);

        var delete = new Button
        {
            Content = "Delete object",
            Foreground = UiTheme.Danger,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        delete.Click += (_, _) => DeleteRequested?.Invoke();
        _stack.Children.Add(delete);
    }

    static Border Chip(string text) => new()
    {
        Background = UiTheme.Surface,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(9, 3),
        Margin = new Thickness(0, 0, 6, 6),
        Child = Ui.Label(text, 11),
    };

    void BuildLevelAndGame()
    {
        _stack.Children.Add(Ui.Label("Level", 15, weight: FontWeight.SemiBold));
        _stack.Children.Add(Ui.Label("Select an object in the level to edit it.", 12, UiTheme.Muted));

        var name = new TextBox { Text = _level.Name };
        name.LostFocus += (_, _) =>
        {
            if (name.Text == _level.Name || string.IsNullOrWhiteSpace(name.Text)) return;
            Edit(() => _level.Name = name.Text!.Trim());
            LevelsChanged?.Invoke();
        };
        _stack.Children.Add(Ui.Field("Name", name));

        var size = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 8 };
        var w = Ui.Field("Width (tiles)", Ui.Number(_level.Width, 10, 1000, 1, v => Edit(() => _level.Width = (int)v)));
        var h = Ui.Field("Height (tiles)", Ui.Number(_level.Height, 8, 200, 1, v => Edit(() => _level.Height = (int)v)));
        Grid.SetColumn(h, 1);
        size.Children.Add(w);
        size.Children.Add(h);
        _stack.Children.Add(size);

        _stack.Children.Add(Ui.Field("Sky colour", SkyPicker()));
        MusicPicker();

        var deleteLevel = new Button { Content = "Delete level", Foreground = UiTheme.Danger, IsEnabled = _project.Levels.Count > 1 };
        deleteLevel.Click += (_, _) =>
        {
            Edit(() => _project.Levels.Remove(_level));
            LevelsChanged?.Invoke();
        };
        _stack.Children.Add(deleteLevel);

        _stack.Children.Add(Ui.Divider());
        _stack.Children.Add(Ui.Label("Game", 15, weight: FontWeight.SemiBold));

        var gameName = new TextBox { Text = _project.Name };
        gameName.LostFocus += (_, _) =>
        {
            if (gameName.Text == _project.Name || string.IsNullOrWhiteSpace(gameName.Text)) return;
            Edit(() => _project.Name = gameName.Text!.Trim());
        };
        _stack.Children.Add(Ui.Field("Game name", gameName));

        _stack.Children.Add(Ui.Heading("Variables"));
        _stack.Children.Add(Ui.Label("Numbers your events can change, shown on screen while playing.", 12, UiTheme.Muted));
        foreach (var key in _project.Variables.Keys.ToList()) _stack.Children.Add(VariableRow(key));

        var add = Ui.IconButton(Ui.Icons.Plus, "Add a variable", () =>
        {
            var n = 1;
            while (_project.Variables.ContainsKey($"Var{n}")) n++;
            Edit(() => _project.Variables[$"Var{n}"] = 0);
            Rebuild();
        }, "Add variable");
        _stack.Children.Add(add);
    }

    void MusicPicker()
    {
        var names = new List<string> { "None" };
        names.AddRange(_project.Sounds.Select(s => s.Name));
        var combo = new ComboBox
        {
            ItemsSource = names,
            SelectedIndex = Math.Max(0, names.IndexOf(_level.Music)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        combo.SelectionChanged += (_, _) =>
        {
            var music = combo.SelectedIndex <= 0 ? "" : names[combo.SelectedIndex];
            if (music != _level.Music) Edit(() => _level.Music = music);
        };
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = _level.MusicVolume * 100 };
        volume.ValueChanged += (_, e) => Edit(() => _level.MusicVolume = e.NewValue / 100);
        _stack.Children.Add(Ui.Field("Music", combo));
        if (_project.Sounds.Count == 0) _stack.Children.Add(Ui.Label("Add or import sounds on the Sounds tab.", 12, UiTheme.Muted));
        else _stack.Children.Add(Ui.Field("Music volume", volume));
    }

    Control SkyPicker()
    {
        var swatches = new[] { "#8ED6FF", "#BFE8FF", "#FFD6A5", "#C3B1E1", "#2B2F4A", "#14161F" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var box = new TextBox { Text = _level.Background, Width = 92 };
        box.LostFocus += (_, _) =>
        {
            if (box.Text is { } t && t != _level.Background && Color.TryParse(t, out _)) Edit(() => _level.Background = t);
        };
        row.Children.Add(box);
        foreach (var s in swatches)
        {
            var color = s;
            var b = new Button
            {
                Width = 22, Height = 22, Padding = new Thickness(0), CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.Parse(color)),
                BorderBrush = UiTheme.Border, BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
            };
            b.Click += (_, _) =>
            {
                Edit(() => _level.Background = color);
                box.Text = color;
            };
            row.Children.Add(b);
        }
        return row;
    }

    Control VariableRow(string key)
    {
        var grid = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,90,Auto"), ColumnGap = 6 };
        var name = new TextBox { Text = key };
        name.LostFocus += (_, _) =>
        {
            var newName = (name.Text ?? "").Trim().Replace(" ", "");
            if (newName == key || newName.Length == 0 || _project.Variables.ContainsKey(newName)) { name.Text = key; return; }
            Edit(() =>
            {
                var value = _project.Variables[key];
                _project.Variables.Remove(key);
                _project.Variables[newName] = value;
                RenameInEvents(key, newName);
            });
            Rebuild();
        };
        var value = Ui.Number(_project.Variables[key], -1e9, 1e9, 1, v => Edit(() => _project.Variables[key] = v));
        value.ShowButtonSpinner = false;
        ToolTip.SetTip(value, "Starting value");
        var remove = Ui.IconButton(Ui.Icons.Close, "Remove variable", () =>
        {
            Edit(() => _project.Variables.Remove(key));
            Rebuild();
        });
        Grid.SetColumn(value, 1);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(name);
        grid.Children.Add(value);
        grid.Children.Add(remove);
        return grid;
    }

    void RenameInEvents(string from, string to)
    {
        foreach (var item in _project.Events.SelectMany(e => e.Conditions.Concat(e.Actions)))
        {
            if (item.Args.TryGetValue("Var", out var v) && v == from) item.Args["Var"] = to;
            if (item.Args.TryGetValue("Text", out var t)) item.Args["Text"] = t.Replace("{" + from + "}", "{" + to + "}");
        }
    }
}
