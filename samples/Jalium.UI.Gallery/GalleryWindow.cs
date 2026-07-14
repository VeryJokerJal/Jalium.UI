using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// The control gallery main window: a single scrollable page that crams every
/// Jalium.UI control into categorized "cards" so a screenshot of this page is a
/// complete visual catalog of the framework (used across the READMEs).
///
/// The window chrome (page background, section headers, cards) uses a fixed dark
/// palette that matches the default <see cref="ThemeVariant.Dark"/> theme; the
/// showcased controls themselves are left at their default theme so the gallery
/// reflects exactly how each control looks out of the box.
///
/// This is a <c>partial</c> class: each category lives in its own
/// <c>GalleryWindow.&lt;Category&gt;.cs</c> file and exposes a
/// <c>public static UIElement Build&lt;Category&gt;Section()</c> method that
/// returns a section built with the <see cref="Section"/> / <see cref="Card"/>
/// helpers defined here.
/// </summary>
internal static partial class GalleryWindow
{
    // ── Dark-theme-matched chrome palette ───────────────────────────────────
    internal static readonly Brush PageBackground   = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
    internal static readonly Brush CardBackground    = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
    internal static readonly Brush CardStroke        = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    internal static readonly Brush HeaderBackground  = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16));
    internal static readonly Brush TextPrimary       = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    internal static readonly Brush TextSecondary     = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8));
    internal static readonly Brush Accent            = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x7A)); // bright green accent

    /// <summary>Builds the gallery window with every control section.</summary>
    public static Window Build()
    {
        var window = new Window
        {
            Title = "Jalium.UI — Control Gallery",
            Width = 1280,
            Height = 900,
            Background = PageBackground,
        };

        // Vertical stack of category sections.
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(28, 20, 28, 40),
        };

        content.Children.Add(PageHeader());

        foreach (var section in CollectSections())
            content.Children.Add(section);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        };

        window.Content = scroller;
        return window;
    }

    /// <summary>
    /// Returns every category section in display order. Each entry is produced by a
    /// <c>Build&lt;Category&gt;Section()</c> method defined in a partial file.
    /// Filled in once the per-category files are generated.
    /// </summary>
    private static IEnumerable<UIElement> CollectSections()
    {
        // Each section lives in its own GalleryWindow.<Category>.cs partial file.
        yield return BuildButtonsSection();
        yield return BuildSelectionSection();
        yield return BuildTextInputSection();
        yield return BuildTextDisplaySection();
        yield return BuildEditorsSection();
        yield return BuildStatusSection();
        yield return BuildPickersSection();
        yield return BuildDataControlsSection();
        yield return BuildContainersSection();
        yield return BuildPanelsSection();
        yield return BuildNavigationSection();
        yield return BuildFlyoutsSection();
        yield return BuildChartsSection();
        yield return BuildDiagramsSection();
        yield return BuildSpecializedSection();
        yield return BuildDialogsSection();
    }

    // ── Reusable chrome helpers (used by every section file) ─────────────────

    /// <summary>Top-of-page title block.</summary>
    private static UIElement PageHeader()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(new TextBlock
        {
            Text = "Jalium.UI Control Gallery",
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimary,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Every control in the framework, on one page — GPU-accelerated, WPF-style UI for .NET 10.",
            FontSize = 14,
            Foreground = TextSecondary,
        });
        return stack;
    }

    /// <summary>
    /// A category section: an accent-barred header + a wrapping row of cards.
    /// Section files call this to return their section.
    /// </summary>
    internal static UIElement Section(string title, string subtitle, params UIElement[] cards)
    {
        var outer = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Margin = new Thickness(0, 18, 0, 6) };

        // Header row: accent bar + title (+ optional subtitle).
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        headerRow.Children.Add(new Border
        {
            Background = Accent,
            Width = 4,
            CornerRadius = new CornerRadius(2),
        });
        var titleStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary });
        if (!string.IsNullOrEmpty(subtitle))
            titleStack.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = TextSecondary });
        headerRow.Children.Add(titleStack);
        outer.Children.Add(headerRow);

        var flow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalSpacing = 12,
            VerticalSpacing = 12,
        };
        foreach (var card in cards)
            flow.Children.Add(card);
        outer.Children.Add(flow);

        return outer;
    }

    /// <summary>
    /// Wraps a single control demo in a titled card. <paramref name="width"/> lets a
    /// wide control (charts, editors) opt out of the default fixed width.
    /// </summary>
    internal static UIElement Card(string title, UIElement content, double width = 300, double minHeight = 0)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
        });
        stack.Children.Add(content);

        var border = new Border
        {
            Background = CardBackground,
            BorderBrush = CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = stack,
        };
        if (!double.IsNaN(width) && width > 0)
            border.Width = width;
        if (minHeight > 0)
            border.MinHeight = minHeight;
        return border;
    }

    /// <summary>
    /// A neutral placeholder shown for controls that need a live external resource
    /// (a web page, a shell process, a camera, a media file, network map tiles…) to
    /// render anything meaningful. It keeps the gallery 100% safe to construct for a
    /// static screenshot while still documenting the control. Used by section files
    /// via <c>Card("WebView", Placeholder("WebView", "…"))</c>.
    /// </summary>
    internal static UIElement Placeholder(string name, string description)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            MinHeight = 92,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Shown live when running inside your app.",
            FontSize = 11,
            Foreground = Accent,
        });

        return new Border
        {
            Background = HeaderBackground,
            BorderBrush = CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = stack,
        };
    }
}
