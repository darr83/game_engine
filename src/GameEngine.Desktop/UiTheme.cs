using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace GameEngine.Desktop;

/// <summary>The colours and fonts shared by the editor and exported games.</summary>
public static class UiTheme
{
    public static readonly Color BackgroundColor = Color.Parse("#111317");
    public static readonly Color PanelColor = Color.Parse("#171A1F");
    public static readonly Color SurfaceColor = Color.Parse("#1F232A");
    public static readonly Color SurfaceHoverColor = Color.Parse("#272C35");
    public static readonly Color BorderColor = Color.Parse("#2A2F38");
    public static readonly Color TextColor = Color.Parse("#E8EAEF");
    public static readonly Color MutedColor = Color.Parse("#8A91A2");
    public static readonly Color AccentColor = Color.Parse("#4C8DFF");
    public static readonly Color PlayColor = Color.Parse("#2FCB7E");
    public static readonly Color ConditionColor = Color.Parse("#4C8DFF");
    public static readonly Color ActionColor = Color.Parse("#F5A524");
    public static readonly Color DangerColor = Color.Parse("#F25F5C");

    public static readonly IBrush Background = new SolidColorBrush(BackgroundColor);
    public static readonly IBrush Panel = new SolidColorBrush(PanelColor);
    public static readonly IBrush Surface = new SolidColorBrush(SurfaceColor);
    public static readonly IBrush SurfaceHover = new SolidColorBrush(SurfaceHoverColor);
    public static readonly IBrush Border = new SolidColorBrush(BorderColor);
    public static readonly IBrush Text = new SolidColorBrush(TextColor);
    public static readonly IBrush Muted = new SolidColorBrush(MutedColor);
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush AccentSoft = new SolidColorBrush(AccentColor, 0.16);
    public static readonly IBrush Play = new SolidColorBrush(PlayColor);
    public static readonly IBrush Condition = new SolidColorBrush(ConditionColor);
    public static readonly IBrush ConditionSoft = new SolidColorBrush(ConditionColor, 0.12);
    public static readonly IBrush Action = new SolidColorBrush(ActionColor);
    public static readonly IBrush ActionSoft = new SolidColorBrush(ActionColor, 0.12);
    public static readonly IBrush Danger = new SolidColorBrush(DangerColor);

    public static readonly FontFamily UiFont = new("fonts:Inter#Inter");
    public static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>Applies the dark Fluent theme with the Game_Engine palette.</summary>
    public static void Apply(Application app)
    {
        app.RequestedThemeVariant = ThemeVariant.Dark;
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources
        {
            Accent = AccentColor,
            RegionColor = PanelColor,
            AltHigh = BackgroundColor,
            BaseHigh = TextColor,
            ChromeMediumLow = SurfaceColor,
            ChromeMedium = SurfaceColor,
            ChromeLow = SurfaceColor,
        };
        app.Styles.Add(fluent);
        app.Styles.Add(EditorStyles());
    }

    static Styles EditorStyles()
    {
        var styles = new Styles();

        styles.Add(new Style(x => x.OfType<Window>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Background),
                new Setter(TemplatedControl.FontFamilyProperty, UiFont),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
            },
        });

        // Quiet toolbar buttons: transparent until hovered.
        styles.Add(new Style(x => x.OfType<Button>().Class("ghost"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 6)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
            },
        });

        styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
            },
        });

        styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.BackgroundProperty, Surface),
                new Setter(TemplatedControl.BorderBrushProperty, Border),
                new Setter(Layoutable.MinHeightProperty, 30.0),
            },
        });

        styles.Add(new Style(x => x.OfType<NumericUpDown>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.BackgroundProperty, Surface),
                new Setter(TemplatedControl.BorderBrushProperty, Border),
                new Setter(Layoutable.MinHeightProperty, 30.0),
            },
        });

        styles.Add(new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.BackgroundProperty, Surface),
                new Setter(TemplatedControl.BorderBrushProperty, Border),
                new Setter(Layoutable.MinHeightProperty, 30.0),
            },
        });

        return styles;
    }
}
