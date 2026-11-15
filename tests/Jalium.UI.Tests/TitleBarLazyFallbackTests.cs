using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Xunit.Abstractions;

namespace Jalium.UI.Tests;

public sealed class TitleBarLazyFallbackTests
{
    private const int AllocationInstanceCount = 256;

    private readonly ITestOutputHelper _output;

    public TitleBarLazyFallbackTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void FallbackButtons_CaptureLatestStateOnFirstAccessAndTrackLaterChanges()
    {
        var titleBar = new TitleBar
        {
            IsMaximized = true,
            ShowMinimizeButton = false,
            ShowMaximizeButton = false,
            ShowCloseButton = false
        };

        var minimize = Assert.IsType<TitleBarButton>(titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        var maximize = Assert.IsType<TitleBarButton>(titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        var close = Assert.IsType<TitleBarButton>(titleBar.GetButtonByKind(TitleBarButtonKind.Close));

        Assert.Equal(TitleBarButtonKind.Minimize, minimize.Kind);
        Assert.Equal(TitleBarButtonKind.Restore, maximize.Kind);
        Assert.Equal(TitleBarButtonKind.Close, close.Kind);
        Assert.Equal(Visibility.Collapsed, minimize.Visibility);
        Assert.Equal(Visibility.Collapsed, maximize.Visibility);
        Assert.Equal(Visibility.Collapsed, close.Visibility);
        Assert.Same(maximize, titleBar.GetButtonByKind(TitleBarButtonKind.Restore));

        titleBar.IsMaximized = false;
        titleBar.ShowMinimizeButton = true;
        titleBar.ShowMaximizeButton = true;
        titleBar.ShowCloseButton = true;

        Assert.Equal(TitleBarButtonKind.Maximize, maximize.Kind);
        Assert.Equal(Visibility.Visible, minimize.Visibility);
        Assert.Equal(Visibility.Visible, maximize.Visibility);
        Assert.Equal(Visibility.Visible, close.Visibility);
    }

    [Fact]
    public void FallbackButtons_RepeatedAccessAndOnApplyTemplateWireEachClickOnce()
    {
        var titleBar = new TitleBar();
        var minimize = Assert.IsType<TitleBarButton>(titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        var maximize = Assert.IsType<TitleBarButton>(titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        var close = Assert.IsType<TitleBarButton>(titleBar.GetButtonByKind(TitleBarButtonKind.Close));

        Assert.Same(minimize, titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        Assert.Same(maximize, titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        Assert.Same(maximize, titleBar.GetButtonByKind(TitleBarButtonKind.Restore));
        Assert.Same(close, titleBar.GetButtonByKind(TitleBarButtonKind.Close));

        var minimizeClicks = 0;
        var maximizeClicks = 0;
        var closeClicks = 0;
        titleBar.MinimizeClicked += (_, _) => minimizeClicks++;
        titleBar.MaximizeRestoreClicked += (_, _) => maximizeClicks++;
        titleBar.CloseClicked += (_, _) => closeClicks++;

        titleBar.OnApplyTemplate();
        titleBar.OnApplyTemplate();

        minimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, minimize));
        maximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, maximize));
        close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, close));

        Assert.Equal(1, minimizeClicks);
        Assert.Equal(1, maximizeClicks);
        Assert.Equal(1, closeClicks);
    }

    [Fact]
    public void EnumerateButtons_WithoutTemplateButtonsCreatesFallbacksInCaptionOrder()
    {
        var titleBar = new TitleBar { IsMaximized = true };
        titleBar.Template = CreateTemplate(new TemplateParts(), includeMinimize: false, includeMaximize: false, includeClose: false);

        var first = titleBar.EnumerateButtons().ToArray();

        Assert.Collection(
            first,
            button => Assert.Equal(TitleBarButtonKind.Minimize, button.Kind),
            button => Assert.Equal(TitleBarButtonKind.Restore, button.Kind),
            button => Assert.Equal(TitleBarButtonKind.Close, button.Kind));
        Assert.Same(first[0], titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        Assert.Same(first[1], titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        Assert.Same(first[1], titleBar.GetButtonByKind(TitleBarButtonKind.Restore));
        Assert.Same(first[2], titleBar.GetButtonByKind(TitleBarButtonKind.Close));

        var second = titleBar.EnumerateButtons().ToArray();
        Assert.Same(first[0], second[0]);
        Assert.Same(first[1], second[1]);
        Assert.Same(first[2], second[2]);
    }

    [Fact]
    public void PartialTemplate_EnumeratesOnlyRealButtonsAndFallsBackForMissingKind()
    {
        var parts = new TemplateParts();
        var titleBar = new TitleBar
        {
            Template = CreateTemplate(parts, includeMinimize: true, includeMaximize: false, includeClose: true)
        };
        var minimize = Assert.IsType<TitleBarButton>(parts.Minimize);
        var close = Assert.IsType<TitleBarButton>(parts.Close);

        var initialEnumeration = titleBar.EnumerateButtons().ToArray();
        Assert.Equal(2, initialEnumeration.Length);
        Assert.Same(minimize, initialEnumeration[0]);
        Assert.Same(close, initialEnumeration[1]);
        Assert.Same(minimize, titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        Assert.Same(close, titleBar.GetButtonByKind(TitleBarButtonKind.Close));

        var fallbackMaximize = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        Assert.Same(fallbackMaximize, titleBar.GetButtonByKind(TitleBarButtonKind.Restore));

        var enumerationAfterFallbackAccess = titleBar.EnumerateButtons().ToArray();
        Assert.Equal(2, enumerationAfterFallbackAccess.Length);
        Assert.Same(minimize, enumerationAfterFallbackAccess[0]);
        Assert.Same(close, enumerationAfterFallbackAccess[1]);
        Assert.DoesNotContain(fallbackMaximize, enumerationAfterFallbackAccess);
    }

    [Fact]
    public void FullTemplate_TakesPriorityOverExistingFallbacksAndReapplyDoesNotDuplicateEvents()
    {
        var titleBar = new TitleBar();
        var fallbackMinimize = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        var fallbackMaximize = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        var fallbackClose = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Close));

        var parts = new TemplateParts();
        titleBar.Template = CreateTemplate(parts, includeMinimize: true, includeMaximize: true, includeClose: true);
        var minimize = Assert.IsType<TitleBarButton>(parts.Minimize);
        var maximize = Assert.IsType<TitleBarButton>(parts.Maximize);
        var close = Assert.IsType<TitleBarButton>(parts.Close);

        Assert.Same(minimize, titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        Assert.Same(maximize, titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        Assert.Same(maximize, titleBar.GetButtonByKind(TitleBarButtonKind.Restore));
        Assert.Same(close, titleBar.GetButtonByKind(TitleBarButtonKind.Close));
        Assert.NotSame(fallbackMinimize, minimize);
        Assert.NotSame(fallbackMaximize, maximize);
        Assert.NotSame(fallbackClose, close);

        var enumerated = titleBar.EnumerateButtons().ToArray();
        Assert.Equal(3, enumerated.Length);
        Assert.Same(minimize, enumerated[0]);
        Assert.Same(maximize, enumerated[1]);
        Assert.Same(close, enumerated[2]);

        var minimizeClicks = 0;
        var maximizeClicks = 0;
        var closeClicks = 0;
        titleBar.MinimizeClicked += (_, _) => minimizeClicks++;
        titleBar.MaximizeRestoreClicked += (_, _) => maximizeClicks++;
        titleBar.CloseClicked += (_, _) => closeClicks++;

        titleBar.OnApplyTemplate();
        titleBar.OnApplyTemplate();
        minimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, minimize));
        maximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, maximize));
        close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, close));

        Assert.Equal(1, minimizeClicks);
        Assert.Equal(1, maximizeClicks);
        Assert.Equal(1, closeClicks);
    }

    [Fact]
    public void ClearingTemplate_DropsRetiredButtonsAndRestoresExistingFallbacks()
    {
        var titleBar = new TitleBar();
        var fallbackMinimize = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        var fallbackMaximize = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        var fallbackClose = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Close));

        var parts = new TemplateParts();
        titleBar.Template = CreateTemplate(parts, includeMinimize: true, includeMaximize: true, includeClose: true);
        var templateMinimize = Assert.IsType<TitleBarButton>(parts.Minimize);
        var templateMaximize = Assert.IsType<TitleBarButton>(parts.Maximize);
        var templateClose = Assert.IsType<TitleBarButton>(parts.Close);

        var minimizeClicks = 0;
        var maximizeClicks = 0;
        var closeClicks = 0;
        titleBar.MinimizeClicked += (_, _) => minimizeClicks++;
        titleBar.MaximizeRestoreClicked += (_, _) => maximizeClicks++;
        titleBar.CloseClicked += (_, _) => closeClicks++;

        titleBar.Template = null;

        Assert.Same(fallbackMinimize, titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        Assert.Same(fallbackMaximize, titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        Assert.Same(fallbackMaximize, titleBar.GetButtonByKind(TitleBarButtonKind.Restore));
        Assert.Same(fallbackClose, titleBar.GetButtonByKind(TitleBarButtonKind.Close));
        Assert.Collection(
            titleBar.EnumerateButtons(),
            button => Assert.Same(fallbackMinimize, button),
            button => Assert.Same(fallbackMaximize, button),
            button => Assert.Same(fallbackClose, button));

        templateMinimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, templateMinimize));
        templateMaximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, templateMaximize));
        templateClose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, templateClose));

        Assert.Equal(0, minimizeClicks);
        Assert.Equal(0, maximizeClicks);
        Assert.Equal(0, closeClicks);

        fallbackMinimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fallbackMinimize));
        fallbackMaximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fallbackMaximize));
        fallbackClose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fallbackClose));

        Assert.Equal(1, minimizeClicks);
        Assert.Equal(1, maximizeClicks);
        Assert.Equal(1, closeClicks);
    }

    [Fact]
    public void ReplacingTemplate_UsesNewPartsAndFallbackForNewlyMissingButton()
    {
        var firstParts = new TemplateParts();
        var titleBar = new TitleBar
        {
            Template = CreateTemplate(firstParts, includeMinimize: true, includeMaximize: true, includeClose: true)
        };
        var retiredMinimize = Assert.IsType<TitleBarButton>(firstParts.Minimize);
        var retiredMaximize = Assert.IsType<TitleBarButton>(firstParts.Maximize);
        var retiredClose = Assert.IsType<TitleBarButton>(firstParts.Close);

        var replacementParts = new TemplateParts();
        titleBar.Template = CreateTemplate(
            replacementParts,
            includeMinimize: true,
            includeMaximize: false,
            includeClose: true);
        titleBar.IsMaximized = true;

        var replacementMinimize = Assert.IsType<TitleBarButton>(replacementParts.Minimize);
        var replacementClose = Assert.IsType<TitleBarButton>(replacementParts.Close);
        var fallbackMaximize = Assert.IsType<TitleBarButton>(
            titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));

        Assert.Same(replacementMinimize, titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        Assert.Same(fallbackMaximize, titleBar.GetButtonByKind(TitleBarButtonKind.Restore));
        Assert.Equal(TitleBarButtonKind.Restore, fallbackMaximize.Kind);
        Assert.Same(replacementClose, titleBar.GetButtonByKind(TitleBarButtonKind.Close));
        Assert.Collection(
            titleBar.EnumerateButtons(),
            button => Assert.Same(replacementMinimize, button),
            button => Assert.Same(replacementClose, button));

        var minimizeClicks = 0;
        var maximizeClicks = 0;
        var closeClicks = 0;
        titleBar.MinimizeClicked += (_, _) => minimizeClicks++;
        titleBar.MaximizeRestoreClicked += (_, _) => maximizeClicks++;
        titleBar.CloseClicked += (_, _) => closeClicks++;

        retiredMinimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, retiredMinimize));
        retiredMaximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, retiredMaximize));
        retiredClose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, retiredClose));

        Assert.Equal(0, minimizeClicks);
        Assert.Equal(0, maximizeClicks);
        Assert.Equal(0, closeClicks);

        replacementMinimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, replacementMinimize));
        fallbackMaximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, fallbackMaximize));
        replacementClose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, replacementClose));

        Assert.Equal(1, minimizeClicks);
        Assert.Equal(1, maximizeClicks);
        Assert.Equal(1, closeClicks);
    }

    [Fact]
    public void Construction_DefersFallbackAllocationUntilButtonsAreRequested()
    {
        WarmAllocationPaths();

        long constructorBytes = Math.Min(
            MeasureTitleBarAllocations(materializeFallbacks: false),
            MeasureTitleBarAllocations(materializeFallbacks: false));
        long materializedBytes = Math.Min(
            MeasureTitleBarAllocations(materializeFallbacks: true),
            MeasureTitleBarAllocations(materializeFallbacks: true));
        long savedBytes = materializedBytes - constructorBytes;

        _output.WriteLine(
            "{0} TitleBar instances: constructors={1:N0} B; constructors + three fallback requests={2:N0} B; deferred={3:N0} B ({4:N1} B/instance). Managed thread allocation only; this is not process working set.",
            AllocationInstanceCount,
            constructorBytes,
            materializedBytes,
            savedBytes,
            (double)savedBytes / AllocationInstanceCount);

        Assert.True(
            savedBytes >= AllocationInstanceCount * 256L,
            $"Requesting all three fallback buttons added only {savedBytes:N0} bytes across " +
            $"{AllocationInstanceCount} TitleBar instances; constructor allocation was {constructorBytes:N0} bytes.");
    }

    private static void WarmAllocationPaths()
    {
        for (int index = 0; index < 4; index++)
        {
            var constructorOnly = new TitleBar();
            GC.KeepAlive(constructorOnly);

            var materialized = new TitleBar();
            MaterializeFallbacks(materialized);
            GC.KeepAlive(materialized);
        }
    }

    private static long MeasureTitleBarAllocations(bool materializeFallbacks)
    {
        var titleBars = new TitleBar[AllocationInstanceCount];
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < titleBars.Length; index++)
        {
            var titleBar = new TitleBar();
            if (materializeFallbacks)
            {
                MaterializeFallbacks(titleBar);
            }

            titleBars[index] = titleBar;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(titleBars);
        return allocated;
    }

    private static void MaterializeFallbacks(TitleBar titleBar)
    {
        GC.KeepAlive(titleBar.GetButtonByKind(TitleBarButtonKind.Minimize));
        GC.KeepAlive(titleBar.GetButtonByKind(TitleBarButtonKind.Maximize));
        GC.KeepAlive(titleBar.GetButtonByKind(TitleBarButtonKind.Close));
    }

    private static ControlTemplate CreateTemplate(
        TemplateParts parts,
        bool includeMinimize,
        bool includeMaximize,
        bool includeClose)
    {
        var template = new ControlTemplate(typeof(TitleBar));
        template.SetVisualTree(() =>
        {
            var root = new Grid();
            if (includeMinimize)
            {
                parts.Minimize = new TitleBarButton { Name = "PART_MinimizeButton" };
                root.Children.Add(parts.Minimize);
            }

            if (includeMaximize)
            {
                parts.Maximize = new TitleBarButton { Name = "PART_MaximizeButton" };
                root.Children.Add(parts.Maximize);
            }

            if (includeClose)
            {
                parts.Close = new TitleBarButton { Name = "PART_CloseButton" };
                root.Children.Add(parts.Close);
            }

            return root;
        });
        return template;
    }

    private sealed class TemplateParts
    {
        public TitleBarButton? Minimize { get; set; }
        public TitleBarButton? Maximize { get; set; }
        public TitleBarButton? Close { get; set; }
    }
}
