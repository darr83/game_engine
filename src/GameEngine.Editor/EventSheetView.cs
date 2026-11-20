using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GameEngine.Core;
using GameEngine.Desktop;

namespace GameEngine.Editor;

/// <summary>
/// The visual event sheet. Each row is "when these conditions are true, do these actions".
/// A toggle shows the same sheet as C#.
/// </summary>
public sealed class EventSheetView : UserControl
{
    readonly StackPanel _rows = new() { Spacing = 10 };
    readonly TextBox _code;
    readonly ScrollViewer _sheetScroll;
    Project _project = null!;
    bool _showCode;

    public EventSheetView()
    {
        _code = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = UiTheme.CodeFont,
            FontSize = 13,
            Background = UiTheme.Panel,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(24, 20),
            TextWrapping = TextWrapping.NoWrap,
            IsVisible = false,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_code, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        var page = new StackPanel { Spacing = 14, Margin = new Thickness(28, 22, 28, 40), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(Header());
        page.Children.Add(_rows);

        var addEvent = Ui.IconButton(Ui.Icons.Plus, "Add a new event", AddEvent, "Add event");
        addEvent.Classes.Remove("ghost");
        addEvent.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(addEvent);

        _sheetScroll = new ScrollViewer { Content = page };
        var root = new Grid();
        root.Children.Add(_sheetScroll);
        root.Children.Add(_code);
        Content = root;
    }

    public event Action? Changing;
    public event Action? Changed;

    public void Show(Project project)
    {
        _project = project;
        Rebuild();
    }

    public bool ShowCode
    {
        get => _showCode;
        set
        {
            _showCode = value;
            _code.IsVisible = value;
            _sheetScroll.IsVisible = !value;
            if (value) _code.Text = EventCode.ToCSharp(_project);
        }
    }

    Control Header()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(Ui.Label("Event sheet", 20, weight: FontWeight.SemiBold));
        text.Children.Add(Ui.Label("Events run top to bottom, 60 times a second. When every condition on the left is true, the actions on the right happen.", 13, UiTheme.Muted));
        grid.Children.Add(text);
        return grid;
    }

    void Edit(Action change, bool rebuild = true)
    {
        Changing?.Invoke();
        change();
        Changed?.Invoke();
        if (rebuild) Rebuild();
    }

    void Rebuild()
    {
        _rows.Children.Clear();
        if (_project.Events.Count == 0)
        {
            _rows.Children.Add(new Border
            {
                Padding = new Thickness(24),
                CornerRadius = new CornerRadius(10),
                BorderBrush = UiTheme.Border,
                BorderThickness = new Thickness(1),
                Child = Ui.Label("No events yet. Add one to make things happen, for example \"Player touches Coin → Destroy the other object\".", 13, UiTheme.Muted),
            });
        }
        for (var i = 0; i < _project.Events.Count; i++) _rows.Children.Add(Row(_project.Events[i], i));
        if (_showCode) _code.Text = EventCode.ToCSharp(_project);
    }

    void AddEvent() => Edit(() => _project.Events.Add(new GameEvent
    {
        Conditions = { EventCatalog.FindCondition("Collides")!.CreateDefault() },
    }));

    Control Row(GameEvent ev, int index)
    {
        // header: number, comment, enable, move, delete
        var header = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnGap = 10, Margin = new Thickness(14, 8, 8, 6) };
        header.Children.Add(new TextBlock
        {
            Text = (index + 1).ToString(), FontWeight = FontWeight.Bold, Foreground = UiTheme.Muted, VerticalAlignment = VerticalAlignment.Center, MinWidth = 18,
        });
        var comment = new TextBox
        {
            Text = ev.Comment,
            Watermark = "Add a note about this event…",
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontWeight = FontWeight.SemiBold,
            MinHeight = 26,
            Padding = new Thickness(4, 3),
        };
        comment.LostFocus += (_, _) => { if (comment.Text != ev.Comment) Edit(() => ev.Comment = comment.Text ?? "", rebuild: false); };
        Grid.SetColumn(comment, 1);
        header.Children.Add(comment);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var enabled = new ToggleSwitch { IsChecked = ev.Enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(enabled, "Turn this event on or off");
        enabled.IsCheckedChanged += (_, _) => Edit(() => ev.Enabled = enabled.IsChecked == true);
        tools.Children.Add(enabled);
        tools.Children.Add(Ui.IconButton(Ui.Icons.Up, "Move up", () => Move(index, -1)));
        tools.Children.Add(Ui.IconButton(Ui.Icons.Down, "Move down", () => Move(index, 1)));
        tools.Children.Add(Ui.IconButton(Ui.Icons.Trash, "Delete event", () => Edit(() => _project.Events.RemoveAt(index)), iconBrush: UiTheme.Muted));
        Grid.SetColumn(tools, 2);
        header.Children.Add(tools);

        // body: conditions | actions
        var body = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnGap = 0 };
        var conditions = Column("When", UiTheme.Condition, UiTheme.ConditionSoft, ev.Conditions, isCondition: true, ev);
        var actions = Column("Do", UiTheme.Action, UiTheme.ActionSoft, ev.Actions, isCondition: false, ev);
        Grid.SetColumn(actions, 1);
        body.Children.Add(conditions);
        body.Children.Add(actions);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(Ui.Divider());
        stack.Children.Add(body);

        return new Border
        {
            Child = stack,
            Background = UiTheme.Panel,
            BorderBrush = UiTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Opacity = ev.Enabled ? 1 : 0.5,
            ClipToBounds = true,
        };
    }

    void Move(int index, int by)
    {
        var to = index + by;
        if (to < 0 || to >= _project.Events.Count) return;
        Edit(() =>
        {
            var e = _project.Events[index];
            _project.Events.RemoveAt(index);
            _project.Events.Insert(to, e);
        });
    }

    Control Column(string title, IBrush color, IBrush soft, List<EventItem> items, bool isCondition, GameEvent ev)
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(14, 10, 14, 12) };
        stack.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 0.8, Foreground = color,
        });

        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            stack.Children.Add(Chip(_project, items[i], isCondition, color, soft,
                edit: async () =>
                {
                    var result = await EventItemDialog.Ask(TopLevel.GetTopLevel(this) as Window, _project, isCondition, items[index]);
                    if (result is not null) Edit(() => items[index] = result);
                },
                remove: () => Edit(() => items.RemoveAt(index))));
        }

        var add = new Button
        {
            Content = isCondition ? "+ Add condition" : "+ Add action",
            Foreground = UiTheme.Muted,
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 4),
            FontSize = 12,
        };
        add.Click += async (_, _) =>
        {
            var result = await EventItemDialog.Ask(TopLevel.GetTopLevel(this) as Window, _project, isCondition, null);
            if (result is not null) Edit(() => items.Add(result));
        };
        stack.Children.Add(add);

        var border = new Border { Child = stack };
        if (!isCondition)
        {
            border.BorderBrush = UiTheme.Border;
            border.BorderThickness = new Thickness(1, 0, 0, 0);
        }
        return border;
    }

    static Control Chip(Project project, EventItem item, bool isCondition, IBrush color, IBrush soft, Action edit, Action remove)
    {
        var grid = new SpacedGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnGap = 10 };
        grid.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = color, Margin = new Thickness(0, 2) });
        var text = new TextBlock
        {
            Text = EventCatalog.Describe(item, isCondition, project),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiTheme.Text,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var close = Ui.IconButton(Ui.Icons.Close, "Remove", remove, iconBrush: UiTheme.Muted);
        close.Padding = new Thickness(4);
        close.Opacity = 0;
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        var chip = new Border
        {
            Child = grid,
            Background = soft,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5, 4, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(chip, "Click to edit");
        chip.PointerEntered += (_, _) => close.Opacity = 1;
        chip.PointerExited += (_, _) => close.Opacity = 0;
        chip.PointerReleased += (_, e) =>
        {
            if (e.Source is Visual v && IsInside(v, close)) return;
            edit();
        };
        return chip;
    }

    static bool IsInside(Visual v, Visual container)
    {
        for (Visual? x = v; x is not null; x = Avalonia.VisualTree.VisualExtensions.GetVisualParent(x))
            if (x == container) return true;
        return false;
    }
}
