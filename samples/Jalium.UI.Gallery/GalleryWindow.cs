using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Threading;

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
    private const int TotalSectionCount = 16;

    private static readonly DeferredSection[] s_deferredSections =
    [
        new("Gallery.Section.Selection", BuildSelectionSection),
        new(
            "Gallery.Section.TextInput",
            "Text Input",
            "Single/multi-line, masked, numeric, auto-complete and combo entry.",
            CreateTextInputCards()),
        new("Gallery.Section.TextDisplay", BuildTextDisplaySection),
        new("Gallery.Section.Editors", BuildEditorsSection),
        new("Gallery.Section.Status", BuildStatusSection),
        new("Gallery.Section.Pickers", BuildPickersSection),
        new(
            "Gallery.Section.DataControls",
            "Collections & Data",
            "Lists, grids and trees over tiny in-memory sample data.",
            CreateDataControlCards()),
        new("Gallery.Section.Containers", BuildContainersSection),
        new("Gallery.Section.Panels", BuildPanelsSection),
        new("Gallery.Section.Navigation", BuildNavigationSection),
        new("Gallery.Section.Flyouts", BuildFlyoutsSection),
        new("Gallery.Section.Charts", BuildChartsSection),
        new("Gallery.Section.Diagrams", BuildDiagramsSection),
        new(
            "Gallery.Section.Specialized",
            "Media & Specialized",
            "Drawing, codes, viewers, editors and resource-backed hosts.",
            CreateSpecializedCards()),
        new("Gallery.Section.Dialogs", BuildDialogsSection),
    ];

    // ── Dark-theme-matched chrome palette ───────────────────────────────────
    internal static readonly Brush PageBackground   = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
    internal static readonly Brush CardBackground    = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
    internal static readonly Brush CardStroke        = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    internal static readonly Brush HeaderBackground  = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16));
    internal static readonly Brush TextPrimary       = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    internal static readonly Brush TextSecondary     = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8));
    internal static readonly Brush Accent            = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x7A)); // bright green accent

    /// <summary>
    /// Builds an immediately usable gallery window. The first interactive section is
    /// available for the initial frame; the remaining catalog sections are added at
    /// background dispatcher priority after the message pump starts.
    /// </summary>
    public static Window Build()
    {
        Window window;
        using (StartupDiagnostics.Begin("Gallery.CreateWindow", blocksUiThread: true))
        {
            window = new Window
            {
                Title = "Jalium.UI — Control Gallery",
                Width = 1280,
                Height = 900,
                Background = PageBackground,
            };
        }

        StackPanel content;
        TextBlock loadingStatus;
        using (StartupDiagnostics.Begin("Gallery.BuildInitialContent", blocksUiThread: true))
        {
            // Vertical stack of category sections.
            content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Margin = new Thickness(28, 20, 28, 40),
            };

            content.Children.Add(PageHeader());
            content.Children.Add(BuildSection("Gallery.Section.Buttons", BuildButtonsSection));

            loadingStatus = new TextBlock
            {
                Text = $"Loading the remaining control groups… (1/{TotalSectionCount})",
                FontSize = 12,
                Foreground = TextSecondary,
                Margin = new Thickness(0, 6, 0, 0),
            };
            AutomationProperties.SetAutomationId(loadingStatus, "GalleryStartupStatus");
            AutomationProperties.SetName(loadingStatus, loadingStatus.Text);
            content.Children.Add(loadingStatus);
        }

        using (StartupDiagnostics.Begin("Gallery.AttachInitialContent", blocksUiThread: true))
        {
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            };

            window.Content = scroller;
        }

        var deferredLoadCancellation = new CancellationTokenSource();
        var deferredLoadStarted = false;
        window.Shown += (_, _) =>
        {
            if (deferredLoadStarted)
                return;

            deferredLoadStarted = true;
            _ = LoadDeferredSectionsAsync(
                content,
                loadingStatus,
                deferredLoadCancellation.Token);
        };
        window.Closed += (_, _) =>
        {
            deferredLoadCancellation.Cancel();
            deferredLoadCancellation.Dispose();
        };

        return window;
    }

    private static async Task LoadDeferredSectionsAsync(
        StackPanel content,
        TextBlock loadingStatus,
        CancellationToken cancellationToken)
    {
        using var deferredLoad = StartupDiagnostics.Begin(
            "Gallery.DeferredSections",
            blocksUiThread: false);

        var loadedCount = 1;
        var failureCount = 0;
        StartupDiagnostics.Mark("Gallery.DeferredSectionsStarted", blocksUiThread: false);

        try
        {
            foreach (var descriptor in s_deferredSections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                loadingStatus.Text =
                    $"Loading control groups… ({loadedCount}/{TotalSectionCount})";
                AutomationProperties.SetName(loadingStatus, loadingStatus.Text);

                // Input, rendering, and the first dispatcher responsiveness probe all
                // run at higher priority. This also presents the loading state before
                // the next group is constructed on the UI-affine object model.
                await Dispatcher.Yield(DispatcherPriority.Background);
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (descriptor.Cards == null)
                    {
                        content.Children.Add(BuildSection(
                            descriptor.StageName,
                            descriptor.Factory!));
                    }
                    else
                    {
                        failureCount += await LoadProgressiveSectionAsync(
                            content,
                            loadingStatus,
                            descriptor,
                            cancellationToken);
                    }

                    loadedCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failureCount++;
                    StartupDiagnostics.Mark(
                        "Gallery.DeferredSectionFailed",
                        blocksUiThread: true);
                    Console.Error.WriteLine(
                        $"[Gallery startup] Deferred section '{descriptor.StageName}' failed: {exception}");
                }
            }

            loadingStatus.Text = failureCount == 0
                ? $"All {TotalSectionCount} control groups loaded."
                : $"Loaded {loadedCount}/{TotalSectionCount} control groups; {failureCount} items failed.";
            AutomationProperties.SetName(loadingStatus, loadingStatus.Text);

            if (failureCount == 0)
                content.Children.Remove(loadingStatus);

            StartupDiagnostics.Mark("Gallery.DeferredSectionsCompleted", blocksUiThread: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupDiagnostics.Mark("Gallery.DeferredSectionsCanceled", blocksUiThread: false);
        }
        catch (Exception exception)
        {
            // This task is intentionally fire-and-forget from the Shown event. Keep an
            // unexpected infrastructure failure observable and contained so it cannot
            // become an unobserved task exception or take down the already usable shell.
            StartupDiagnostics.Mark("Gallery.DeferredSectionsFailed", blocksUiThread: false);
            Console.Error.WriteLine(
                $"[Gallery startup] Deferred loading stopped unexpectedly: {exception}");

            try
            {
                loadingStatus.Text = "Some control groups could not be loaded.";
                AutomationProperties.SetName(loadingStatus, loadingStatus.Text);
            }
            catch
            {
                // Reporting is best-effort if the visual tree is already tearing down.
            }
        }
    }

    private static async Task<int> LoadProgressiveSectionAsync(
        StackPanel content,
        TextBlock loadingStatus,
        DeferredSection descriptor,
        CancellationToken cancellationToken)
    {
        using var section = StartupDiagnostics.Begin(
            descriptor.StageName,
            blocksUiThread: false);

        var (sectionRoot, cardsHost) = CreateSectionChrome(
            descriptor.Title!,
            descriptor.Subtitle!);
        content.Children.Add(sectionRoot);

        var failureCount = 0;
        foreach (var card in descriptor.Cards!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadingStatus.Text = $"Loading {descriptor.Title}… {card.Title}";
            AutomationProperties.SetName(loadingStatus, loadingStatus.Text);

            await Dispatcher.Yield(DispatcherPriority.Background);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var cardConstruction = StartupDiagnostics.Begin(
                    card.StageName,
                    blocksUiThread: true);
                cardsHost.Children.Add(Card(
                    card.Title,
                    card.Factory(),
                    card.Width,
                    card.MinHeight));
            }
            catch (Exception exception)
            {
                failureCount++;
                StartupDiagnostics.Mark("Gallery.DeferredCardFailed", blocksUiThread: true);
                Console.Error.WriteLine(
                    $"[Gallery startup] Deferred card '{card.StageName}' failed: {exception}");
            }
        }

        return failureCount;
    }

    private static UIElement BuildSection(string stageName, Func<UIElement> factory)
    {
        using var section = StartupDiagnostics.Begin(stageName, blocksUiThread: true);
        return factory();
    }

    private sealed class DeferredSection
    {
        public DeferredSection(string stageName, Func<UIElement> factory)
        {
            StageName = stageName;
            Factory = factory;
        }

        public DeferredSection(
            string stageName,
            string title,
            string subtitle,
            DeferredCard[] cards)
        {
            StageName = stageName;
            Title = title;
            Subtitle = subtitle;
            Cards = cards;
        }

        public string StageName { get; }
        public Func<UIElement>? Factory { get; }
        public string? Title { get; }
        public string? Subtitle { get; }
        public DeferredCard[]? Cards { get; }
    }

    private readonly record struct DeferredCard(
        string StageName,
        string Title,
        Func<UIElement> Factory,
        double Width = 300,
        double MinHeight = 0);

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
        var (outer, flow) = CreateSectionChrome(title, subtitle);
        foreach (var card in cards)
            flow.Children.Add(card);

        return outer;
    }

    private static UIElement SectionFromCards(
        string title,
        string subtitle,
        IReadOnlyList<DeferredCard> cards)
    {
        var (outer, flow) = CreateSectionChrome(title, subtitle);
        foreach (var card in cards)
            flow.Children.Add(Card(card.Title, card.Factory(), card.Width, card.MinHeight));

        return outer;
    }

    private static (StackPanel Outer, WrapPanel CardsHost) CreateSectionChrome(
        string title,
        string subtitle)
    {
        var outer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Margin = new Thickness(0, 18, 0, 6),
        };

        // Header row: accent bar + title (+ optional subtitle).
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        headerRow.Children.Add(new Border
        {
            Background = Accent,
            Width = 4,
            CornerRadius = new CornerRadius(2),
        });
        var titleStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
        });
        if (!string.IsNullOrEmpty(subtitle))
            titleStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Foreground = TextSecondary,
            });
        headerRow.Children.Add(titleStack);
        outer.Children.Add(headerRow);

        var flow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalSpacing = 12,
            VerticalSpacing = 12,
        };
        outer.Children.Add(flow);
        return (outer, flow);
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
