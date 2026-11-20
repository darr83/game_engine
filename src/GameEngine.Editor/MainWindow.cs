using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

public sealed class MainWindow : Window
{
    Project _project;
    string? _path;
    bool _dirty;
    int _levelIndex;

    readonly UndoStack _undo = new();
    readonly LevelCanvas _canvas;
    readonly ObjectLibrary _library = new();
    readonly Inspector _inspector = new();
    readonly EventSheetView _events = new();
    readonly ComboBox _levelPicker = new() { MinWidth = 170 };
    readonly TextBlock _title = new() { FontSize = 13, Foreground = UiTheme.Muted, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _status = new() { FontSize = 12, Foreground = UiTheme.Muted, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _coords = new() { FontSize = 12, Foreground = UiTheme.Muted, VerticalAlignment = VerticalAlignment.Center };
    readonly Dictionary<Tool, Button> _toolButtons = new();
    readonly Panel _levelTools;
    readonly Panel _eventTools;
    readonly Grid _levelArea;
    Func<int, bool> _selectTab = _ => true;
    bool _updatingPicker;

    public MainWindow() : this(SampleProject.Create()) { }

    public MainWindow(Project project)
    {
        _project = project;
        Title = "Game_Engine";
        Width = 1440;
        Height = 880;
        MinWidth = 1000;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _canvas = new LevelCanvas(_project, CurrentLevel);
        _levelTools = BuildLevelTools();
        _eventTools = BuildEventTools();
        _eventTools.IsVisible = false;

        // centre: level canvas or event sheet
        _levelArea = new Grid();
        _levelArea.Children.Add(_canvas);
        _events.IsVisible = false;
        var center = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        center.Children.Add(CenterBar());
        var content = new Grid();
        content.Children.Add(_levelArea);
        content.Children.Add(_events);
        Grid.SetRow(content, 1);
        center.Children.Add(content);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,300") };
        body.Children.Add(Side(_library, right: false));
        Grid.SetColumn(center, 1);
        body.Children.Add(center);
        var inspector = Side(_inspector, right: true);
        Grid.SetColumn(inspector, 2);
        body.Children.Add(inspector);

        var root = new DockPanel();
        var top = TopBar();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var status = StatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(body);
        Content = root;

        Wire();
        LoadProject(_project, null);
        SetTool(Tool.Select);
    }

    Level CurrentLevel => _project.Levels[Math.Clamp(_levelIndex, 0, _project.Levels.Count - 1)];

    // ------------------------------------------------------------------ layout

    Control TopBar()
    {
        var logo = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = UiTheme.Accent,
            Child = new TextBlock
            {
                Text = "GE", FontSize = 11, FontWeight = FontWeight.Black, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(logo);
        brand.Children.Add(Ui.Label("Game_Engine", 14, weight: FontWeight.Bold));
        brand.Children.Add(_title);

        var file = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(18, 0, 0, 0) };
        file.Children.Add(Ui.IconButton(Ui.Icons.New, "New game (Ctrl+N)", () => _ = NewProject()));
        file.Children.Add(Ui.IconButton(Ui.Icons.Open, "Open… (Ctrl+O)", () => _ = Open()));
        file.Children.Add(Ui.IconButton(Ui.Icons.Save, "Save (Ctrl+S)", () => _ = Save()));
        file.Children.Add(Ui.Divider(Orientation.Vertical));
        file.Children.Add(Ui.IconButton(Ui.Icons.Undo, "Undo (Ctrl+Z)", Undo));
        file.Children.Add(Ui.IconButton(Ui.Icons.Redo, "Redo (Ctrl+Y)", Redo));

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(brand);
        left.Children.Add(file);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Ui.IconButton(Ui.Icons.Export, "Export a stand-alone game", () => _ = Export(), "Export"));
        right.Children.Add(Ui.Primary(Ui.Icons.Play, "Play", UiTheme.Play, Play));
        ToolTip.SetTip(right.Children[^1], "Play from this level (F5)");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 0) };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return new Border
        {
            Height = 54,
            Background = UiTheme.Panel,
            BorderBrush = UiTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    Control CenterBar()
    {
        var tabs = Ui.Segmented(new[] { "Level", "Event sheet" }, 0, ShowTab, out _selectTab);

        var grid = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 8), ColumnGap = 12 };
        grid.Children.Add(tabs);
        var middle = new Grid();
        middle.Children.Add(_levelTools);
        middle.Children.Add(_eventTools);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        var levelBox = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        levelBox.Children.Add(_levelPicker);
        levelBox.Children.Add(Ui.IconButton(Ui.Icons.Plus, "Add a level", AddLevel));
        Grid.SetColumn(levelBox, 2);
        grid.Children.Add(levelBox);

        return new Border
        {
            Background = UiTheme.Panel,
            BorderBrush = UiTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    Panel BuildLevelTools()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        void Add(Tool t, string icon, string label, string tip)
        {
            var b = Ui.IconButton(icon, tip, () => SetTool(t), label);
            _toolButtons[t] = b;
            row.Children.Add(b);
        }
        Add(Tool.Select, Ui.Icons.Select, "Select", "Select and move objects (V)");
        Add(Tool.Paint, Ui.Icons.Paint, "Paint", "Paint the chosen object (B). Right-drag erases.");
        Add(Tool.Erase, Ui.Icons.Erase, "Erase", "Erase objects (E)");
        row.Children.Add(Ui.Divider(Orientation.Vertical));
        row.Children.Add(Ui.IconButton(Ui.Icons.Fit, "Fit level to view", () => _canvas.FitToView()));
        return row;
    }

    Panel BuildEventTools()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => _events.ShowCode = toggle.IsChecked == true;
        row.Children.Add(Ui.Icon(Ui.Icons.Code, 15, UiTheme.Muted));
        row.Children.Add(Ui.Label("Show as C#", 13, UiTheme.Muted));
        row.Children.Add(toggle);
        return row;
    }

    static Control Side(Control content, bool right) => new Border
    {
        Background = UiTheme.Panel,
        BorderBrush = UiTheme.Border,
        BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0),
        Child = content,
    };

    Control StatusBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 0) };
        grid.Children.Add(_status);
        Grid.SetColumn(_coords, 1);
        grid.Children.Add(_coords);
        return new Border
        {
            Height = 28,
            Background = UiTheme.Panel,
            BorderBrush = UiTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
    }

    // ------------------------------------------------------------------ wiring

    void Wire()
    {
        _canvas.Changing += Snapshot;
        _canvas.Changed += () => { MarkDirty(); _inspector.Refresh(); };
        _canvas.SelectionChanged += () => _inspector.Show(_project, CurrentLevel, _canvas.Selected);
        _canvas.StatusChanged += s => _coords.Text = s;

        _inspector.Changing += Snapshot;
        _inspector.Changed += () => { MarkDirty(); _canvas.InvalidateVisual(); };
        _inspector.LevelsChanged += () => { _levelIndex = Math.Min(_levelIndex, _project.Levels.Count - 1); RefreshLevels(); };
        _inspector.DeleteRequested += () => _canvas.DeleteSelected();

        _events.Changing += Snapshot;
        _events.Changed += () => { EnsureVariables(); MarkDirty(); };

        _library.Picked += type =>
        {
            _canvas.PaintType = type;
            SetTool(Tool.Paint);
            ShowTab(0);
            _selectTab(0);
            SetStatus($"Painting {ObjectCatalog.Get(type).Name}. Click or drag in the level. Right-drag to erase.");
        };

        _levelPicker.SelectionChanged += (_, _) =>
        {
            if (_updatingPicker || _levelPicker.SelectedIndex < 0) return;
            _levelIndex = _levelPicker.SelectedIndex;
            _canvas.SetLevel(_project, CurrentLevel);
        };

        AddHandler(KeyDownEvent, OnShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    void OnShortcut(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var typing = FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox or NumericUpDown;

        if (ctrl && e.Key == Key.S) { _ = Save(); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { _ = Open(); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { _ = NewProject(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z && !typing) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y && !typing) { Redo(); e.Handled = true; }
        else if (e.Key == Key.F5) { Play(); e.Handled = true; }
        else if (!typing && !ctrl)
        {
            if (e.Key == Key.V) SetTool(Tool.Select);
            else if (e.Key == Key.B) SetTool(Tool.Paint);
            else if (e.Key == Key.E) SetTool(Tool.Erase);
        }
    }

    /// <summary>0 = level, 1 = event sheet.</summary>
    public void SelectTab(int index)
    {
        ShowTab(index);
        _selectTab(index);
    }

    void ShowTab(int index)
    {
        _levelArea.IsVisible = index == 0;
        _levelTools.IsVisible = index == 0;
        _events.IsVisible = index == 1;
        _eventTools.IsVisible = index == 1;
        if (index == 1) _events.Show(_project);
    }

    void SetTool(Tool tool)
    {
        _canvas.Tool = tool;
        foreach (var (t, b) in _toolButtons)
        {
            b.Background = t == tool ? UiTheme.AccentSoft : Brushes.Transparent;
            b.Foreground = t == tool ? UiTheme.Text : UiTheme.Muted;
        }
        _library.Highlight(tool == Tool.Paint ? _canvas.PaintType : null);
        _canvas.InvalidateVisual();
        if (tool == Tool.Select) SetStatus("Drag objects from the left into the level. Scroll to zoom, middle-drag or Space+drag to pan.");
        if (tool == Tool.Erase) SetStatus("Click or drag over objects to erase them.");
    }

    void SetStatus(string text) => _status.Text = text;

    // ------------------------------------------------------------------ project state

    void LoadProject(Project project, string? path)
    {
        _project = project;
        _path = path;
        _levelIndex = 0;
        _undo.Clear();
        _dirty = false;
        RefreshLevels();
        _events.Show(_project);
        UpdateTitle();
    }

    void RefreshLevels()
    {
        _updatingPicker = true;
        _levelPicker.ItemsSource = _project.Levels.Select((l, i) => $"{i + 1}. {l.Name}").ToList();
        _levelPicker.SelectedIndex = _levelIndex;
        _updatingPicker = false;
        _canvas.SetLevel(_project, CurrentLevel);
    }

    void AddLevel()
    {
        Snapshot();
        var template = SampleProject.CreateBlank().Levels[0];
        template.Name = $"Level {_project.Levels.Count + 1}";
        _project.Levels.Add(template);
        _levelIndex = _project.Levels.Count - 1;
        RefreshLevels();
        MarkDirty();
        ShowTab(0);
        _selectTab(0);
    }

    /// <summary>Variables named in events but not yet defined start at 0.</summary>
    void EnsureVariables()
    {
        foreach (var item in _project.Events.SelectMany(e => e.Conditions.Concat(e.Actions)))
            if (item.Args.TryGetValue("Var", out var v) && v.Length > 0 && !_project.Variables.ContainsKey(v))
                _project.Variables[v] = 0;
    }

    void Snapshot() => _undo.Push(_project, _levelIndex);

    void Undo()
    {
        if (_undo.Undo(_project, _levelIndex) is not { } state) return;
        Restore(state);
        SetStatus("Undone.");
    }

    void Redo()
    {
        if (_undo.Redo(_project, _levelIndex) is not { } state) return;
        Restore(state);
        SetStatus("Redone.");
    }

    void Restore((Project project, int level) state)
    {
        var selectedId = _canvas.Selected?.Id;
        _project = state.project;
        _levelIndex = Math.Clamp(state.level, 0, _project.Levels.Count - 1);
        RefreshLevels();
        _events.Show(_project);
        if (selectedId is { } id) _canvas.Select(CurrentLevel.Instances.FirstOrDefault(i => i.Id == id));
        MarkDirty();
    }

    void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
    }

    void UpdateTitle()
    {
        var name = _path is null ? _project.Name : Path.GetFileNameWithoutExtension(_path);
        _title.Text = $"·  {name}{(_dirty ? " •" : "")}";
        Title = $"{name}{(_dirty ? " •" : "")} — Game_Engine";
    }

    // ------------------------------------------------------------------ commands

    void Play()
    {
        var window = new GameWindow(_project, _levelIndex, fromEditor: true);
        window.Show(this);
        SetStatus("Playing. Arrow keys or A/D to run, Space to jump. Esc stops, R restarts.");
    }

    async Task NewProject()
    {
        LoadProject(SampleProject.CreateBlank(), null);
        SetStatus("Started a new game.");
        await Task.CompletedTask;
    }

    static readonly FilePickerFileType ProjectFileType = new("Game_Engine project") { Patterns = new[] { "*" + ProjectIO.Extension } };

    async Task Open()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a game",
            AllowMultiple = false,
            FileTypeFilter = new[] { ProjectFileType },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            LoadProject(ProjectIO.Load(path), path);
            SetStatus($"Opened {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    async Task<bool> Save()
    {
        if (_path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save game",
                SuggestedFileName = _project.Name + ProjectIO.Extension,
                DefaultExtension = ProjectIO.Extension.TrimStart('.'),
                FileTypeChoices = new[] { ProjectFileType },
            });
            if (file?.TryGetLocalPath() is not { } path) return false;
            _path = path;
        }
        try
        {
            ProjectIO.Save(_project, _path);
            _dirty = false;
            UpdateTitle();
            SetStatus($"Saved to {_path}.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save: {ex.Message}");
            return false;
        }
    }

    async Task Export()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder for the exported game",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;
        var result = Exporter.Export(_project, folder);
        SetStatus(result.Message);
    }
}
