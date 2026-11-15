using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;
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
    private const int TotalSectionCount = 17;

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
        new("Gallery.Section.Effects", BuildEffectsSection),
    ];

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
#pragma warning disable WPF0001 // This catalog uses a fixed dark palette.
                ThemeMode = ThemeMode.Dark,
#pragma warning restore WPF0001
            };
        }

        GallerySampleSurface content;
        GallerySampleStatus loadingStatus;
        using (StartupDiagnostics.Begin("Gallery.BuildInitialContent", blocksUiThread: true))
        {
            content = new GallerySampleSurface();
            window.SetResourceReference(Control.BackgroundProperty, "SampleGallery.PageBackground");

            content.Items.Add(PageHeader());
            content.Items.Add(BuildSection("Gallery.Section.Buttons", BuildButtonsSection));

            loadingStatus = new GallerySampleStatus
            {
                Text = $"Loading the remaining control groups… (1/{TotalSectionCount})",
            };
            AutomationProperties.SetAutomationId(loadingStatus, "GalleryStartupStatus");
            AutomationProperties.SetName(loadingStatus, loadingStatus.Text);
            content.Items.Add(loadingStatus);
        }

        using (StartupDiagnostics.Begin("Gallery.AttachInitialContent", blocksUiThread: true))
        {
            window.Content = content;
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
        GallerySampleSurface content,
        GallerySampleStatus loadingStatus,
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
                        content.Items.Add(BuildSection(
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
                    System.Diagnostics.Debug.WriteLine(
                        $"[Gallery startup] Deferred section '{descriptor.StageName}' failed: {exception}");
                }
            }

            loadingStatus.Text = failureCount == 0
                ? $"All {TotalSectionCount} control groups loaded."
                : $"Loaded {loadedCount}/{TotalSectionCount} control groups; {failureCount} items failed.";
            AutomationProperties.SetName(loadingStatus, loadingStatus.Text);

            if (failureCount == 0)
                content.Items.Remove(loadingStatus);

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
            System.Diagnostics.Debug.WriteLine(
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
        GallerySampleSurface content,
        GallerySampleStatus loadingStatus,
        DeferredSection descriptor,
        CancellationToken cancellationToken)
    {
        using var section = StartupDiagnostics.Begin(
            descriptor.StageName,
            blocksUiThread: false);

        var (sectionRoot, cardsHost) = CreateSectionChrome(
            descriptor.Title!,
            descriptor.Subtitle!);
        content.Items.Add(sectionRoot);

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
                cardsHost.Add(Card(
                    card.Title,
                    card.Factory(),
                    card.Width,
                    card.MinHeight));
            }
            catch (Exception exception)
            {
                failureCount++;
                StartupDiagnostics.Mark("Gallery.DeferredCardFailed", blocksUiThread: true);
                System.Diagnostics.Debug.WriteLine(
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
    private static UIElement PageHeader() => new GallerySampleHeader
    {
        Title = "Jalium.UI Control Gallery",
        Description = "Every control in the framework, on one page — GPU-accelerated, WPF-style UI for .NET 10.",
    };

    /// <summary>
    /// A category section: an accent-barred header + a wrapping row of cards.
    /// Section files call this to return their section.
    /// </summary>
    internal static UIElement Section(string title, string subtitle, params UIElement[] cards)
    {
        var (outer, flow) = CreateSectionChrome(title, subtitle);
        foreach (var card in cards)
            flow.Add(card);

        return outer;
    }

    private static UIElement SectionFromCards(
        string title,
        string subtitle,
        IReadOnlyList<DeferredCard> cards)
    {
        var (outer, flow) = CreateSectionChrome(title, subtitle);
        foreach (var card in cards)
            flow.Add(Card(card.Title, card.Factory(), card.Width, card.MinHeight));

        return outer;
    }

    private static (GallerySampleSection Outer, ItemCollection CardsHost) CreateSectionChrome(
        string title, string subtitle)
    {
        var section = new GallerySampleSection { Title = title, Description = subtitle };
        return (section, section.Items);
    }

    /// <summary>
    /// Wraps a single control demo in a titled card. <paramref name="width"/> lets a
    /// wide control (charts, editors) opt out of the default fixed width.
    /// </summary>
    internal static UIElement Card(string title, UIElement content, double width = 300, double minHeight = 0)
        => new GallerySampleCard
        {
            Header = title, Content = content,
            PreferredWidth = !double.IsNaN(width) && width > 0 ? width : double.NaN,
            MinHeight = Math.Max(0, minHeight),
        };

    /// <summary>
    /// A neutral placeholder shown for controls that need a live external resource
    /// (a web page, a shell process, a camera, a media file, network map tiles…) to
    /// render anything meaningful. It keeps the gallery 100% safe to construct for a
    /// static screenshot while still documenting the control. Used by section files
    /// via <c>Card("WebView", Placeholder("WebView", "…"))</c>.
    /// </summary>
    internal static UIElement Placeholder(string name, string description)
        => new GallerySamplePlaceholder { Title = name, Description = description };
}
