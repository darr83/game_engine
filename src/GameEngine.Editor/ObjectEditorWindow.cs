using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

/// <summary>
/// Edits one object type: its name, sprite and animations, behaviours and their settings,
/// and its own variables. Works on a copy; returns the edited copy when saved.
/// </summary>
public sealed class ObjectEditorWindow : Window
{
    readonly ObjectType _type;
    readonly Project _project;
    readonly SpriteEditor _sprite;
    readonly StackPanel _behaviours = new() { Spacing = 10, Margin = new Thickness(28, 20, 28, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _variables = new() { Spacing = 10, Margin = new Thickness(28, 20, 28, 28), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _pages = new();
    readonly ScrollViewer _behaviourPage, _variablePage;
    readonly Image _icon = new();
    Func<int, bool> _selectTab = _ => true;
    bool _edited;

    public ObjectEditorWindow(Project project, ObjectType type, bool isNew = false)
    {
        _project = project;
        _type = type.Clone();
        Title = isNew ? "New object" : $"Edit {type.Name}";
        Width = 1240;
        Height = 820;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _sprite = new SpriteEditor(_type);
        _sprite.Edited += () => { _edited = true; RefreshIcon(); };
        _behaviourPage = new ScrollViewer { Content = _behaviours, IsVisible = false };
        _variablePage = new ScrollViewer { Content = _variables, IsVisible = false };
        _pages.Children.Add(_sprite);
        _pages.Children.Add(_behaviourPage);
        _pages.Children.Add(_variablePage);

        var root = new DockPanel();
        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_pages);
        Content = root;

        BuildBehaviours();
        BuildVariables();
        RefreshIcon();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_sprite.IsVisible && FocusManager?.GetFocusedElement() is not (TextBox or NumericUpDown or AutoCompleteBox))
                _sprite.HandleKey(e);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the editor; returns the edited object, or null if cancelled.</summary>
    public static Task<ObjectType?> Edit(Window owner, Project project, ObjectType type, bool isNew = false) =>
        new ObjectEditorWindow(project, type, isNew).ShowDialog<ObjectType?>(owner);

    /// <summary>The working copy (for tests and previews).</summary>
    public ObjectType Working => _type;

    public SpriteEditor Sprite => _sprite;

    public void ShowPage(int index)
    {
        _sprite.IsVisible = index == 0;
        _behaviourPage.IsVisible = index == 1;
        _variablePage.IsVisible = index == 2;
        _selectTab(index);
    }

    // ------------------------------------------------------------------ header and footer

    Control Header()
    {
        RenderOptions.SetBitmapInterpolationMode(_icon, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        var iconBox = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(8), Background = UiTheme.Surface, Child = _icon, Padding = new Thickness(5) };

        var name = new TextBox { Text = _type.Name, Width = 220, FontSize = 15, FontWeight = FontWeight.SemiBold };
        name.TextChanged += (_, _) => { _type.Name = (name.Text ?? "").Trim(); _edited = true; };
        var categories = BuiltInObjects.Categories.Concat(_project.Objects.Select(o => o.Category)).Append("My objects").Distinct().ToList();
        var category = new AutoCompleteBox { Text = _type.Category, ItemsSource = categories, MinimumPrefixLength = 0, Width = 160, FilterMode = AutoCompleteFilterMode.StartsWith };
        category.TextChanged += (_, _) => { _type.Category = string.IsNullOrWhiteSpace(category.Text) ? "My objects" : category.Text.Trim(); _edited = true; };
        var description = new TextBox { Text = _type.Description, Watermark = "What is this object for?", MinWidth = 260 };
        description.TextChanged += (_, _) => { _type.Description = description.Text ?? ""; _edited = true; };

        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        fields.Children.Add(iconBox);
        fields.Children.Add(Ui.Field("Name", name));
        fields.Children.Add(Ui.Field("Group", category));
        fields.Children.Add(Ui.Field("Description", description));

        var tabs = Ui.Segmented(new[] { "Sprite", "Behaviour", "Variables" }, 0, ShowPage, out _selectTab);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 12) };
        grid.Children.Add(fields);
        Grid.SetColumn(tabs, 1);
        grid.Children.Add(tabs);
        return new Border
        {
            Background = UiTheme.Panel, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
        };
    }

    Control Footer()
    {
        var note = Ui.Label($"Id in events and levels: {_type.Id}", 12, UiTheme.Muted);
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 7) };
        var confirming = false;
        cancel.Click += (_, _) =>
        {
            if (_edited && !confirming)
            {
                // ask once, in place, before throwing work away
                confirming = true;
                cancel.Content = "Discard changes";
                cancel.Foreground = UiTheme.Danger;
                return;
            }
            Close(null);
        };
        var save = Ui.Primary(Ui.Icons.Check, "Save object", UiTheme.Accent, () =>
        {
            if (string.IsNullOrWhiteSpace(_type.Name)) _type.Name = _type.Id;
            Close(_type);
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 10) };
        grid.Children.Add(note);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return new Border { Background = UiTheme.Panel, BorderBrush = UiTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
    }

    void RefreshIcon() => _icon.Source = SpriteCache.Get(_type.Icon);

    // ------------------------------------------------------------------ behaviours

    void BuildBehaviours()
    {
        _behaviours.Children.Clear();
        _behaviours.Children.Add(Ui.Label("Behaviour", 20, weight: FontWeight.SemiBold));
        _behaviours.Children.Add(Ui.Label("Tick the behaviours this object should have; any combination works. Settings here are the defaults for every copy in your levels; a single copy can still be changed in the inspector. For anything else, use the event sheet.", 13, UiTheme.Muted));

        foreach (var info in BehaviourCatalog.All)
            _behaviours.Children.Add(BehaviourCard(info));
    }

    Control BehaviourCard(BehaviourInfo info)
    {
        var on = _type.Has(info.Flag);
        var check = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Top };
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(Ui.Label(info.Name, 14, weight: FontWeight.SemiBold));
        title.Children.Add(Ui.Label(info.Description, 12, UiTheme.Muted));

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        head.Children.Add(check);
        Grid.SetColumn(title, 1);
        head.Children.Add(title);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(head);

        if (on && info.Settings.Length > 0)
        {
            var settings = new WrapPanel { Margin = new Thickness(32, 4, 0, 0) };
            foreach (var p in info.Settings)
            {
                var prop = p;
                var field = new StackPanel { Spacing = 4, Width = 200, Margin = new Thickness(0, 0, 16, 10) };
                field.Children.Add(Ui.Label(p.Label, 12, UiTheme.Muted));
                field.Children.Add(Ui.Number(_type.Setting(p.Name), p.Min, p.Max, p.Step, v => { _type.Settings[prop.Name] = v; _edited = true; }));
                if (p.Help.Length > 0) field.Children.Add(Ui.Label(p.Help, 11, UiTheme.Muted));
                settings.Children.Add(field);
            }
            body.Children.Add(settings);
        }

        check.IsCheckedChanged += (_, _) =>
        {
            _type.Behaviours = check.IsChecked == true ? _type.Behaviours | info.Flag : _type.Behaviours & ~info.Flag;
            _edited = true;
            BuildBehaviours();
        };

        return new Border
        {
            Child = body,
            Padding = new Thickness(16, 12),
            CornerRadius = new CornerRadius(10),
            Background = on ? UiTheme.Surface : UiTheme.Panel,
            BorderBrush = on ? UiTheme.Accent : UiTheme.Border,
            BorderThickness = new Thickness(1),
        };
    }

    // ------------------------------------------------------------------ variables

    void BuildVariables()
    {
        _variables.Children.Clear();
        _variables.Children.Add(Ui.Label("Object variables", 20, weight: FontWeight.SemiBold));
        _variables.Children.Add(Ui.Label("Numbers each copy of this object keeps for itself, such as Health or Ammo. Change and check them in the event sheet with “Add to object variable” and “Compare object variable”.", 13, UiTheme.Muted));

        foreach (var key in _type.Variables.Keys.ToList())
        {
            var k = key;
            var grid = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,140,Auto"), ColumnGap = 8 };
            var name = new TextBox { Text = k };
            name.LostFocus += (_, _) =>
            {
                var n = new string((name.Text ?? "").Where(char.IsLetterOrDigit).ToArray());
                if (n.Length == 0 || n == k || _type.Variables.ContainsKey(n)) { name.Text = k; return; }
                var v = _type.Variables[k];
                _type.Variables.Remove(k);
                _type.Variables[n] = v;
                _edited = true;
                BuildVariables();
            };
            var value = Ui.Number(_type.Variables[k], -1e9, 1e9, 1, v => { _type.Variables[k] = v; _edited = true; });
            ToolTip.SetTip(value, "Starting value");
            var remove = Ui.IconButton(Ui.Icons.Close, "Remove", () => { _type.Variables.Remove(k); _edited = true; BuildVariables(); });
            Grid.SetColumn(value, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(name);
            grid.Children.Add(value);
            grid.Children.Add(remove);
            _variables.Children.Add(grid);
        }

        var add = Ui.IconButton(Ui.Icons.Plus, "Add a variable", () =>
        {
            var names = new[] { "Health", "Ammo", "Speed", "Timer" };
            var n = names.FirstOrDefault(x => !_type.Variables.ContainsKey(x)) ?? $"Var{_type.Variables.Count + 1}";
            _type.Variables[n] = n == "Health" ? 3 : 0;
            _edited = true;
            BuildVariables();
        }, "Add variable");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        _variables.Children.Add(add);
    }
}
