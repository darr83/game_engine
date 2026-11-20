using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GameEngine.Desktop;

namespace GameEngine.Editor;

/// <summary>Small factory helpers so the UI code stays readable.</summary>
public static class Ui
{
    public static class Icons
    {
        public const string Play = "M7 4 L20 12 L7 20 Z";
        public const string Plus = "M11 4h2v7h7v2h-7v7h-2v-7H4v-2h7z";
        public const string Trash = "M9 3h6v2h5v2H4V5h5z M6 8h12l-1 13H7z";
        public const string Select = "M5 3 L19 12 L12.5 13.5 L16 20 L13.5 21 L10 14.5 L5 19 Z";
        public const string Paint = "M4 17.5V20h2.5L17 9.5 14.5 7z M15.5 6l2.5 2.5 1.5-1.5L17 4.5z";
        public const string Erase = "M3 15.5 L10.5 8 L17 14.5 L13 18.5 H6 Z M11.5 7 L15 3.5 L21.5 10 L18 13.5 Z M3 20h18v1.5H3z";
        public const string Undo = "M9 5 L3 10 L9 15 V11.5 C14 11.5 17 13 19 17 C18.5 12 15.5 8.5 9 8.5 Z";
        public const string Redo = "M15 5 L21 10 L15 15 V11.5 C10 11.5 7 13 5 17 C5.5 12 8.5 8.5 15 8.5 Z";
        public const string Save = "M5 3h11l3 3v15H5z M8 5v4h7V5z M8 13v6h8v-6z";
        public const string Open = "M3 5h7l2 2h9v12H3z";
        public const string New = "M6 2h8l5 5v15H6z M13 3.5V8h4.5z";
        public const string Export = "M12 3 L17 8 H13.5 V14 H10.5 V8 H7 Z M4 15h3v3h10v-3h3v6H4z";
        public const string Code = "M8 7 L3 12 L8 17 L9.4 15.6 L5.8 12 L9.4 8.4 Z M16 7 L21 12 L16 17 L14.6 15.6 L18.2 12 L14.6 8.4 Z";
        public const string Up = "M12 6 L19 14 H5 Z";
        public const string Down = "M12 18 L5 10 H19 Z";
        public const string Close = "M6.4 5 L12 10.6 L17.6 5 L19 6.4 L13.4 12 L19 17.6 L17.6 19 L12 13.4 L6.4 19 L5 17.6 L10.6 12 L5 6.4 Z";
        public const string Check = "M9 16.2 L4.8 12 L3.4 13.4 L9 19 L21 7 L19.6 5.6 Z";
        public const string Fit = "M4 4h6v2H6v4H4z M14 4h6v6h-2V6h-4z M4 14h2v4h4v2H4z M18 14h2v6h-6v-2h4z";
    }

    public static PathIcon Icon(string data, double size = 16, IBrush? brush = null)
    {
        var icon = new PathIcon { Data = Geometry.Parse(data), Width = size, Height = size };
        if (brush is not null) icon.Foreground = brush;
        return icon;
    }

    public static TextBlock Label(string text, double size = 13, IBrush? brush = null, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? UiTheme.Text,
        FontWeight = weight,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Small uppercase heading used for panel sections.</summary>
    public static TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.8,
        Foreground = UiTheme.Muted,
        Margin = new Thickness(0, 4, 0, 2),
    };

    public static Button IconButton(string icon, string tooltip, Action onClick, string? label = null, IBrush? iconBrush = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(Icon(icon, 15, iconBrush));
        if (label is not null) content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("ghost");
        ToolTip.SetTip(b, tooltip);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Primary(string icon, string label, IBrush background, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(Icon(icon, 14, Brushes.White));
        content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Content = content,
            Background = background,
            Padding = new Thickness(16, 7),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>A label above an input.</summary>
    public static Control Field(string label, Control input)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Label(label, 12, UiTheme.Muted));
        panel.Children.Add(input);
        return panel;
    }

    public static NumericUpDown Number(double value, double min, double max, double step, Action<double> changed)
    {
        var n = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)step,
            FormatString = "0.##",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        n.ValueChanged += (_, e) => { if (e.NewValue is { } v) changed((double)v); };
        return n;
    }

    public static Border Divider(Orientation o = Orientation.Horizontal) => new()
    {
        Background = UiTheme.Border,
        Height = o == Orientation.Horizontal ? 1 : double.NaN,
        Width = o == Orientation.Vertical ? 1 : double.NaN,
        Margin = o == Orientation.Vertical ? new Thickness(6, 8) : new Thickness(0),
    };

    /// <summary>A segmented toggle, like "Level | Event sheet".</summary>
    public static Border Segmented(string[] options, int selected, Action<int> onSelect, out Func<int, bool> select)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var buttons = new List<ToggleButton>();
        void Pick(int i)
        {
            for (var j = 0; j < buttons.Count; j++)
            {
                buttons[j].IsChecked = j == i;
                buttons[j].Foreground = j == i ? UiTheme.Text : UiTheme.Muted;
                buttons[j].Background = j == i ? UiTheme.SurfaceHover : Brushes.Transparent;
            }
        }
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var t = new ToggleButton
            {
                Content = options[i],
                Padding = new Thickness(14, 5),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeight.Medium,
            };
            t.Click += (_, _) => { Pick(index); onSelect(index); };
            buttons.Add(t);
            row.Children.Add(t);
        }
        Pick(selected);
        select = i => { Pick(i); return true; };
        return new Border
        {
            Child = row,
            Background = UiTheme.Background,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}

/// <summary>A grid with a gap between columns (children must get their column before being added).</summary>
public sealed class SpacedGrid : Grid
{
    public double ColumnGap { get; set; }

    public SpacedGrid()
    {
        Children.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (Control c in e.NewItems)
                if (GetColumn(c) > 0)
                    c.Margin = new Thickness(c.Margin.Left + ColumnGap, c.Margin.Top, c.Margin.Right, c.Margin.Bottom);
        };
    }
}
