using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>Convenience adapters for DevTools data and actions. Composite visuals live in the control themes.</summary>
internal static class DevToolsUi
{
    public static TextBlock Text(string content, double size = DevToolsTheme.FontBase, Brush? color = null, FontWeight? weight = null, bool mono = false)
    {
        var text = new TextBlock { Text = content, Style = DevToolsThemeResources.GetStyle(mono ? "DevTools.MonoText" : "DevTools.Text"), FontSize = size };
        if (mono) ApplyStandaloneMonoFallback(text);
        if (color != null) text.Foreground = color;
        if (weight.HasValue) text.FontWeight = weight.Value;
        return text;
    }
    public static TextBlock Muted(string content, double size = DevToolsTheme.FontSm) =>
        new() { Text = content, Style = DevToolsThemeResources.GetStyle("DevTools.MutedText"), FontSize = size };
    public static TextBlock Mono(string content, double size = DevToolsTheme.FontSm, Brush? color = null) => Text(content, size, color, mono: true);
    public static string Tracked(string s) => string.IsNullOrEmpty(s) || s.Length < 2 ? s ?? string.Empty : string.Join(' ', s.ToCharArray());
    public static TextBlock SectionHeading(string content) => new() { Text = Tracked(content.ToUpperInvariant()), Style = DevToolsThemeResources.GetStyle("DevTools.SectionHeading") };
    public static TextBlock PanelHeading(string content) => new() { Text = content.ToUpperInvariant(), Style = DevToolsThemeResources.GetStyle("DevTools.PanelHeading") };
    public static TextBlock Eyebrow(string content, Brush? color = null)
    {
        var text = new TextBlock { Text = Tracked(content.ToUpperInvariant()), Style = DevToolsThemeResources.GetStyle("DevTools.Eyebrow") };
        if (color != null) text.Foreground = color;
        return text;
    }

    public static DevToolsSurface Card(UIElement child, bool alternate = false) => new() { Content = child, Kind = alternate ? DevToolsSurfaceKind.AlternateCard : DevToolsSurfaceKind.Card };
    public static DevToolsSurface CornerTicks(UIElement child, Brush? tick = null, double size = 9)
    {
        var surface = new DevToolsSurface { Content = child, Kind = DevToolsSurfaceKind.CornerFrame, TickSize = size };
        if (tick != null) surface.TickBrush = tick;
        return surface;
    }
    public static DevToolsStatusIndicator StatusLed(Brush color, bool on = true, double size = 8) => new() { Foreground = color, IsOn = on, Width = size, Height = size };
    public static DevToolsSurface Panel(string title, UIElement body, Brush? led = null, bool ledOn = true) => new() { Kind = DevToolsSurfaceKind.Panel, Heading = title, Content = body, IndicatorBrush = led, IsIndicatorOn = ledOn };
    public static DevToolsSurface Toolbar(UIElement child) => new() { Kind = DevToolsSurfaceKind.Toolbar, Content = child };
    public static DevToolsSurface StatusBar(UIElement child) => new() { Kind = DevToolsSurfaceKind.StatusBar, Content = child };
    public static DevToolsActionButton Button(string label, Action onClick, DevToolsButtonVariant style = DevToolsButtonVariant.Default, string? icon = null)
    {
        var button = new DevToolsActionButton { Label = label, Variant = style, Icon = icon };
        button.Click += (_, _) => onClick();
        return button;
    }
    public static DevToolsActionButton Toggle(string label, Action onClick, bool isActive, string? icon = null)
    {
        var button = Button(label, onClick, icon: icon);
        button.IsActive = isActive;
        return button;
    }
    public static TextBlock GridCell(string text, int column, Brush? color = null, bool mono = false)
    {
        var cell = new TextBlock { Text = text, Style = DevToolsThemeResources.GetStyle(mono ? "DevTools.MonoGridCell" : "DevTools.GridCell") };
        if (mono) ApplyStandaloneMonoFallback(cell);
        if (color != null) cell.Foreground = color;
        Grid.SetColumn(cell, column);
        return cell;
    }
    public static DevToolsSurface GridRow(bool alt, UIElement content) => new() { Kind = alt ? DevToolsSurfaceKind.AlternateRow : DevToolsSurfaceKind.Row, Content = content };
    public static DevToolsReadout KeyValueRow(string key, string value, Brush? valueColor = null)
    {
        var readout = new DevToolsReadout { Label = key, ValueText = value };
        if (valueColor != null) readout.ValueBrush = valueColor;
        return readout;
    }
    public static DevToolsMeter Meter(double fraction, Brush fill, double height = 5, Brush? track = null)
    {
        var meter = new DevToolsMeter { Value = DevToolsMeterRow.NormalizeFraction(fraction), ProgressBrush = fill, Height = height };
        if (track != null) meter.Background = track;
        return meter;
    }
    public static DevToolsMeterRow MeterBar(string label, double fraction, string valueText, Brush fill) =>
        new() { Label = label, Fraction = fraction, ValueText = valueText, Foreground = fill };
    public static DevToolsStackedBar StackedBar(IReadOnlyList<(double Value, Brush Color)> segments, double height = 10) =>
        new() { ItemsSource = segments.Select(segment => new DevToolsBarSegment(segment.Value, segment.Color)).ToArray(), Height = height };
    public static TextBox TextInput(double width = double.NaN, string? placeholder = null)
    {
        var input = new TextBox { Style = DevToolsThemeResources.GetStyle("DevTools.TextInput") };
        if (!double.IsNaN(width)) input.Width = width;
        if (placeholder != null) input.PlaceholderText = placeholder;
        return input;
    }
    public static Border VerticalDivider(double height = 16) => new() { Height = height, Style = DevToolsThemeResources.GetStyle("DevTools.VerticalDivider") };
    public static UIElement Spacer(double width = DevToolsTheme.GutterBase) => new Border { Width = width };
    public static DevToolsSegmentedSelector Segmented(params (string Glyph, string? Tooltip, Action OnSelect)[] segments)
    {
        var selector = new DevToolsSegmentedSelector();
        foreach (var segment in segments) selector.AddSegment(segment.Glyph, segment.Tooltip, segment.OnSelect);
        return selector;
    }
    public static DevToolsTag Pill(string text, Brush foreground, Brush? fill = null)
    {
        var tag = new DevToolsTag { Text = text, Foreground = foreground };
        if (fill != null) tag.Background = fill;
        return tag;
    }

    private static void ApplyStandaloneMonoFallback(TextBlock text)
    {
        // Preserve the dynamic style reference while supplying a concrete value
        // before an Application or caller scope provides typography resources.
        if (text.TryFindResource("MonoFontFamily") is null)
            text.SetCurrentValue(TextBlock.FontFamilyProperty, new FontFamily(ThemeManager.CurrentMonospaceFontFamily));
    }
}

/// <summary>Sealed leaf styles are available before Application construction, just like grid-column defaults.</summary>
internal static class DevToolsThemeResources
{
    private static readonly object Gate = new();
    private static ResourceDictionary? _defaults;
    internal static Style GetStyle(string key)
    {
        var dictionary = Volatile.Read(ref _defaults);
        if (dictionary == null)
        {
            lock (Gate)
            {
                dictionary = _defaults;
                if (dictionary == null)
                {
                    dictionary = ThemeManager.LoadControlThemeDictionary("Themes/Controls/DevToolsPrimitives.jalxaml");
                    foreach (var resourceKey in dictionary.Keys.Cast<object>().ToArray())
                        if (dictionary.TryGetValue(resourceKey, out var value) && value is Style style) style.Seal();
                    Volatile.Write(ref _defaults, dictionary);
                }
            }
        }
        return dictionary.TryGetValue(key, out var resource) && resource is Style result ? result : throw new InvalidOperationException($"Missing DevTools style '{key}'.");
    }
}
