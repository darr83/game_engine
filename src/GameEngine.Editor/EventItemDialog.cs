using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

/// <summary>Picks a condition or action and fills in its settings.</summary>
public sealed class EventItemDialog : Window
{
    readonly Project _project;
    readonly bool _isCondition;
    readonly IReadOnlyList<EventKind> _kinds;
    readonly StackPanel _form = new() { Spacing = 12 };
    readonly TextBlock _preview = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly ListBox _list;
    EventItem _item;

    public EventItemDialog(Project project, bool isCondition, EventItem? existing)
    {
        _project = project;
        _isCondition = isCondition;
        _kinds = isCondition ? EventCatalog.Conditions : EventCatalog.Actions;
        _item = existing is null ? _kinds[0].CreateDefault() : Copy(existing);

        Title = isCondition ? "Condition" : "Action";
        Width = 720;
        Height = 480;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var accent = isCondition ? UiTheme.Condition : UiTheme.Action;

        // left: list of kinds grouped by category
        _list = new ListBox { Background = Brushes.Transparent };
        string? lastCategory = null;
        var items = new List<Control>();
        foreach (var k in _kinds)
        {
            if (k.Category != lastCategory)
            {
                var h = Ui.Heading(k.Category);
                h.Margin = new Thickness(4, items.Count == 0 ? 0 : 10, 0, 2);
                items.Add(new ListBoxItem { Content = h, IsEnabled = false, Padding = new Thickness(8, 2), MinHeight = 0 });
                lastCategory = k.Category;
            }
            items.Add(new ListBoxItem { Content = k.Name, Tag = k, Padding = new Thickness(12, 7), CornerRadius = new CornerRadius(6) });
        }
        _list.ItemsSource = items;
        _list.SelectedItem = items.FirstOrDefault(i => (i as ListBoxItem)?.Tag is EventKind k && k.Kind == _item.Kind);
        _list.SelectionChanged += (_, _) =>
        {
            if ((_list.SelectedItem as ListBoxItem)?.Tag is not EventKind kind || kind.Kind == _item.Kind) return;
            // keep settings that the new kind also has
            var next = kind.CreateDefault();
            foreach (var p in kind.Params)
                if (_item.Args.TryGetValue(p.Key, out var v) && Compatible(p, v)) next.Args[p.Key] = v;
            _item = next;
            BuildForm();
        };

        var left = new Border
        {
            Width = 240,
            Background = UiTheme.Background,
            Padding = new Thickness(10),
            Child = _list,
        };

        // right: settings + preview
        var previewBox = new Border
        {
            Background = isCondition ? UiTheme.ConditionSoft : UiTheme.ActionSoft,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Child = _preview,
        };
        var ok = Ui.Primary(existing is null ? Ui.Icons.Plus : Ui.Icons.Check, existing is null ? "Add" : "Save", accent, () => Close(_item));
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 7) };
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var right = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);
        var top = new StackPanel { Spacing = 16 };
        top.Children.Add(Ui.Label(isCondition ? "When…" : "Do…", 12, accent, FontWeight.Bold));
        top.Children.Add(previewBox);
        top.Children.Add(_form);
        right.Children.Add(new ScrollViewer { Content = top });

        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        Content = root;

        BuildForm();
    }

    public static async Task<EventItem?> Ask(Window? owner, Project project, bool isCondition, EventItem? existing)
    {
        var dialog = new EventItemDialog(project, isCondition, existing);
        if (owner is null) { dialog.Show(); return null; }
        return await dialog.ShowDialog<EventItem?>(owner);
    }

    static EventItem Copy(EventItem e) => new() { Kind = e.Kind, Args = new Dictionary<string, string>(e.Args) };

    static bool Compatible(ParamDef p, string value) => p.Type switch
    {
        ParamType.ObjectType => ObjectCatalog.Find(value) is not null,
        ParamType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        ParamType.Target => value is "A" or "B",
        _ => true,
    };

    EventKind Kind => (_isCondition ? EventCatalog.FindCondition(_item.Kind) : EventCatalog.FindAction(_item.Kind)) ?? _kinds[0];

    void UpdatePreview() => _preview.Text = Kind.Describe(_item);

    void BuildForm()
    {
        _form.Children.Clear();
        var kind = Kind;
        if (kind.Params.Length == 0)
            _form.Children.Add(Ui.Label("Nothing to set up for this one.", 13, UiTheme.Muted));

        foreach (var p in kind.Params)
        {
            var param = p;
            var value = _item.Arg(p.Key, p.Default);
            void Set(string v) { _item.Args[param.Key] = v; UpdatePreview(); }

            Control input = p.Type switch
            {
                ParamType.ObjectType => Combo(ObjectCatalog.TypeNames.ToArray(), value, Set),
                ParamType.Key => Combo(EventCatalog.Keys, value, Set),
                ParamType.Compare => Combo(EventCatalog.Comparisons, value, Set),
                ParamType.Target => Combo(new[] { "A", "B" }, value, Set,
                    v => v == "A" ? "The first object (left in the condition)" : "The other object (right in the condition)"),
                ParamType.Variable => VariableBox(value, Set),
                ParamType.Number => NumberBox(value, Set),
                _ => TextField(value, Set),
            };
            _form.Children.Add(Ui.Field(p.Label, input));
            if (p.Type == ParamType.Text)
                _form.Children.Add(Ui.Label("Tip: write {Score} to show a variable's value.", 12, UiTheme.Muted));
        }
        UpdatePreview();
    }

    static ComboBox Combo(string[] values, string value, Action<string> set, Func<string, string>? display = null)
    {
        var box = new ComboBox
        {
            ItemsSource = values.Select(v => display?.Invoke(v) ?? v).ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(values, value)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(values[box.SelectedIndex]); };
        return box;
    }

    Control VariableBox(string value, Action<string> set)
    {
        var box = new AutoCompleteBox
        {
            Text = value,
            ItemsSource = _project.Variables.Keys.ToList(),
            FilterMode = AutoCompleteFilterMode.StartsWithOrdinal,
            MinimumPrefixLength = 0,
            Watermark = "Variable name",
        };
        box.TextChanged += (_, _) => set((box.Text ?? "").Trim().Replace(" ", ""));
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(box);
        panel.Children.Add(Ui.Label("A new name creates a new variable.", 12, UiTheme.Muted));
        return panel;
    }

    static TextBox NumberBox(string value, Action<string> set)
    {
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) =>
        {
            var ok = double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            box.BorderBrush = ok ? UiTheme.Border : UiTheme.Danger;
            if (ok) set(box.Text!.Trim());
        };
        return box;
    }

    static TextBox TextField(string value, Action<string> set)
    {
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) => set(box.Text ?? "");
        return box;
    }
}
