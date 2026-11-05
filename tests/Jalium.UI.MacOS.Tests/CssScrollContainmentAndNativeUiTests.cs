using System.IO;
using System.Linq;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssScrollContainmentAndNativeUiTests : IDisposable
{
    private readonly RenderContext _context;

    public CssScrollContainmentAndNativeUiTests()
    {
        _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        CssScrollProperties.Register();
        CssContainmentProperties.Register();
        CssNativeUiProperties.Register();
    }

    public void Dispose()
    {
    }

    [Fact]
    public void ScrollSnapAlignsMandatoryAndProximityTargetsWithScrollPaddingAndMargin()
    {
        var viewer = new ScrollViewer
        {
            Width = 320,
            Height = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Css.SetStyle(
            viewer,
            "scroll-snap-type: y mandatory; scroll-padding: 20px 0px 10px 0px; scroll-behavior: smooth;");

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var card0 = new Border { Width = 300, Height = 180, Background = Brushes.SlateGray };
        var card1 = new Border { Width = 300, Height = 180, Background = Brushes.SteelBlue };
        var card2 = new Border { Width = 300, Height = 180, Background = Brushes.CadetBlue };
        var card3 = new Border { Width = 300, Height = 180, Background = Brushes.DarkSlateBlue };

        Css.SetStyle(card0, "scroll-snap-align: start;");
        Css.SetStyle(card1, "scroll-snap-align: start; scroll-margin-top: 12px; scroll-snap-stop: always;");
        Css.SetStyle(card2, "scroll-snap-align: center;");
        Css.SetStyle(card3, "scroll-snap-align: end; scroll-margin-bottom: 8px;");

        stack.Children.Add(card0);
        stack.Children.Add(card1);
        stack.Children.Add(card2);
        stack.Children.Add(card3);
        viewer.Content = stack;

        Layout(viewer, 320, 200);

        var snapType = CssScrollProperties.GetScrollSnapType(viewer);
        Assert.Equal(CssScrollSnapAxis.Y, snapType.Axis);
        Assert.Equal(CssScrollSnapStrictness.Mandatory, snapType.Strictness);
        Assert.Equal(CssScrollBehavior.Smooth, CssScrollProperties.GetScrollBehavior(viewer));

        var pad = CssScrollProperties.GetScrollPadding(viewer, new Size(320, 200));
        Assert.Equal(20, pad.Top, 3);
        Assert.Equal(10, pad.Bottom, 3);

        // card1 starts at Y=180, with scroll-margin-top: 12 => snap area top = 168.
        // Snapport top is 20 => aligned start offset = 168 - 20 = 148.
        var snappedToCard1 = CssScrollProperties.ResolveSnapOffset(viewer, new Point(0, 130));
        Assert.Equal(148, snappedToCard1.Y, 3);

        // Verify scroll-snap-stop: always on card1 (offset 148):
        // Starting from offset 0, a large downward flick toward Y=380 with positive velocity must stop at card1 (148).
        CssScrollProperties.SetScrollOffset(viewer, new Point(0, 0), applySnap: false);
        var stoppedAtCard1 = CssScrollProperties.ResolveSnapOffset(viewer, new Point(0, 380), new Vector(0, 450));
        Assert.Equal(148, stoppedAtCard1.Y, 3);

        // card2 is at Y=360..540 (height 180), center = 450.
        // Snapport is Y=20..190 (height 170), center = 20 + 85 = 105.
        // Center-aligned snap offset for card2 = 450 - 105 = 345.
        var snappedToCard2 = CssScrollProperties.ResolveSnapOffset(viewer, new Point(0, 330));
        Assert.Equal(345, snappedToCard2.Y, 3);

        // Switch to proximity strictness: offsets near card1 snap to 148, while offsets far from snap points stay unsnapped.
        Css.SetStyle(viewer, "scroll-snap-type: y proximity; scroll-padding: 20px 0px 10px 0px;");
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);

        var proximityNear = CssScrollProperties.ResolveSnapOffset(viewer, new Point(0, 158));
        Assert.Equal(148, proximityNear.Y, 3);

        var proximityFar = CssScrollProperties.ResolveSnapOffset(viewer, new Point(0, 82));
        Assert.Equal(82, proximityFar.Y, 3);

        // ScrollIntoView respects both scroll-padding on container and scroll-margin on target.
        CssScrollProperties.ScrollIntoView(card1);
        Assert.Equal(148, viewer.VerticalOffset, 3);
    }

    [Fact]
    public void ScrollAnchoringAndOverscrollBehaviorPreserveViewportPositionWithoutReparenting()
    {
        var outerViewer = new ScrollViewer
        {
            Width = 340,
            Height = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollInertiaEnabled = false,
        };

        var outerStack = new StackPanel { Orientation = Orientation.Vertical };
        var innerViewer = new ScrollViewer
        {
            Width = 320,
            Height = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollInertiaEnabled = false,
        };
        Css.SetStyle(innerViewer, "overflow-anchor: auto; overscroll-behavior: contain none;");

        var innerStack = new StackPanel { Orientation = Orientation.Vertical };
        var headerBefore = new Border { Width = 300, Height = 80, Background = Brushes.DarkSlateGray };
        var anchorItem = new Border { Width = 300, Height = 100, Background = Brushes.Teal };
        var tailItem1 = new Border { Width = 300, Height = 140, Background = Brushes.SlateBlue };
        var tailItem2 = new Border { Width = 300, Height = 160, Background = Brushes.Indigo };

        innerStack.Children.Add(headerBefore);
        innerStack.Children.Add(anchorItem);
        innerStack.Children.Add(tailItem1);
        innerStack.Children.Add(tailItem2);
        innerViewer.Content = innerStack;

        outerStack.Children.Add(innerViewer);
        outerStack.Children.Add(new Border { Width = 320, Height = 300, Background = Brushes.DarkBlue });
        outerViewer.Content = outerStack;

        Layout(outerViewer, 340, 260);

        var originalParent = anchorItem.VisualParent;
        Assert.Same(innerStack, originalParent);

        // Scroll innerViewer to Y=100 so anchorItem (Y=80..180) is the active anchor in the viewport.
        innerViewer.ScrollToVerticalOffset(100);
        Layout(outerViewer, 340, 260);
        Assert.Equal(100, innerViewer.VerticalOffset, 3);

        // Grow headerBefore above the viewport by +70px (from 80 to 150).
        // Scroll anchoring must adjust innerViewer.VerticalOffset from 100 to 170 without reparenting.
        headerBefore.Height = 150;
        Layout(outerViewer, 340, 260);

        Assert.Equal(170, innerViewer.VerticalOffset, 3);
        Assert.Same(innerStack, anchorItem.VisualParent);

        // Opt out via overflow-anchor: none; growing headerBefore by +40px must NOT shift VerticalOffset.
        Css.SetStyle(innerViewer, "overflow-anchor: none; overscroll-behavior: contain;");
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);

        headerBefore.Height = 190;
        Layout(outerViewer, 340, 260);
        Assert.Equal(170, innerViewer.VerticalOffset, 3);

        // Test overscroll-behavior: contain on innerViewer at bottom boundary:
        // Scroll innerViewer to its bottom end and dispatch a downward mouse wheel event.
        innerViewer.ScrollToVerticalOffset(innerViewer.ScrollableHeight);
        Layout(outerViewer, 340, 260);
        Assert.Equal(0, outerViewer.VerticalOffset, 3);

        var wheelEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = innerViewer,
        };
        innerViewer.RaiseEvent(wheelEvent);

        Assert.True(wheelEvent.Handled);
        Assert.Equal(0, outerViewer.VerticalOffset, 3);
    }

    [Fact]
    public void ContainSizeLayoutStylePaintAndContainIntrinsicSizeEnforceBoundaries()
    {
        var root = new StackPanel { Orientation = Orientation.Vertical };

        // 1. contain: size without intrinsic size ignores child dimensions during Measure
        var sizeContainedEmpty = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Border { Width = 320, Height = 180 },
        };
        Css.SetStyle(sizeContainedEmpty, "contain: size;");
        root.Children.Add(sizeContainedEmpty);

        // 2. contain: strict with contain-intrinsic-size: 240px 110px
        var strictWithIntrinsic = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Border { Width = 480, Height = 290 },
        };
        Css.SetStyle(strictWithIntrinsic, "contain: strict; contain-intrinsic-size: 240px 110px;");
        root.Children.Add(strictWithIntrinsic);

        // 3. contain: inline-size with contain-intrinsic-inline-size: 185px
        var inlineContained = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Border { Width = 400, Height = 95 },
        };
        Css.SetStyle(inlineContained, "contain: inline-size; contain-intrinsic-inline-size: 185px;");
        root.Children.Add(inlineContained);

        // 4. contain: content (layout | style | paint)
        var contentContained = new Border
        {
            Width = 200,
            Height = 80,
            Child = new Border { Width = 260, Height = 120 },
        };
        Css.SetStyle(contentContained, "contain: content;");
        root.Children.Add(contentContained);

        // 5. contain: style counter isolation check
        var outerBefore = new TextBlock();
        var styleContainedHost = new StackPanel();
        var innerCounterChild = new TextBlock();
        styleContainedHost.Children.Add(innerCounterChild);
        var outerAfter = new TextBlock();

        Css.SetStyle(root, "counter-reset: sec 1;");
        Css.SetStyle(outerBefore, "counter-increment: sec 1;");
        Css.SetStyle(styleContainedHost, "contain: style;");
        Css.SetStyle(innerCounterChild, "counter-increment: sec 5;");
        Css.SetStyle(outerAfter, "counter-increment: sec 1;");

        root.Children.Add(outerBefore);
        root.Children.Add(styleContainedHost);
        root.Children.Add(outerAfter);

        Layout(root, 600, 600);

        Assert.True(CssContainmentProperties.HasSizeContainment(sizeContainedEmpty));
        Assert.Equal(0, sizeContainedEmpty.DesiredSize.Width, 3);
        Assert.Equal(0, sizeContainedEmpty.DesiredSize.Height, 3);

        Assert.True(CssContainmentProperties.HasSizeContainment(strictWithIntrinsic));
        Assert.True(CssContainmentProperties.HasLayoutContainment(strictWithIntrinsic));
        Assert.True(CssContainmentProperties.HasStyleContainment(strictWithIntrinsic));
        Assert.True(CssContainmentProperties.HasPaintContainment(strictWithIntrinsic));
        Assert.Equal(240, strictWithIntrinsic.ActualWidth, 3);
        Assert.Equal(110, strictWithIntrinsic.ActualHeight, 3);
        Assert.True(strictWithIntrinsic.ClipToBounds);
        Assert.True(CssPositionedLayout.EstablishesFixedContainingBlock(strictWithIntrinsic));

        Assert.True(CssContainmentProperties.HasInlineSizeContainment(inlineContained));
        Assert.False(CssContainmentProperties.HasSizeContainment(inlineContained));
        Assert.Equal(185, inlineContained.ActualWidth, 3);
        Assert.Equal(95, inlineContained.ActualHeight, 3);

        Assert.True(CssContainmentProperties.HasLayoutContainment(contentContained));
        Assert.True(CssContainmentProperties.HasStyleContainment(contentContained));
        Assert.True(CssContainmentProperties.HasPaintContainment(contentContained));
        Assert.True(contentContained.ClipToBounds);
        Assert.True(CssPositionedLayout.EstablishesFixedContainingBlock(contentContained));
        Assert.True(CssPositionedLayout.EstablishesAbsoluteContainingBlock(contentContained));

        // Verify that SyncStyleContainmentCounterScope scoped the "sec" counter on the first child of styleContainedHost
        var scopedReset = innerCounterChild.GetValue(CssCounters.ResetProperty) as CssCounterOperations;
        Assert.NotNull(scopedReset);
        Assert.Contains(scopedReset!.Entries, e => e.Name == "sec" && e.Value == 0);
    }

    [Fact]
    public void ContentVisibilityAutoAndHiddenManagePlaceholderSizeAndRevealOnFocus()
    {
        var host = new StackPanel { Orientation = Orientation.Vertical };

        // 1. content-visibility: hidden skips subtree and uses contain-intrinsic-size placeholder
        var hiddenSection = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var hiddenChild = new Border { Width = 360, Height = 175 };
        hiddenSection.Child = hiddenChild;
        Css.SetStyle(hiddenSection, "content-visibility: hidden; contain-intrinsic-size: 210px 85px;");
        host.Children.Add(hiddenSection);

        // 2. content-visibility: auto with contain-intrinsic-size: auto 190px 75px
        var autoSection = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var focusableTarget = new Button { Width = 310, Height = 135, Content = "Inspect Telemetry" };
        autoSection.Child = focusableTarget;
        Css.SetStyle(autoSection, "content-visibility: auto; contain-intrinsic-width: auto 190px; contain-intrinsic-height: auto 75px;");
        host.Children.Add(autoSection);

        Layout(host, 500, 500);

        // Hidden section is skipped and uses 210x85 placeholder
        Assert.True(CssContainmentProperties.IsContentSkipped(hiddenSection));
        Assert.Equal(210, hiddenSection.ActualWidth, 3);
        Assert.Equal(85, hiddenSection.ActualHeight, 3);
        Assert.Equal(1.0, hiddenChild.Opacity, 3); // Skipping preserves authored style values.
        Assert.False(hiddenChild.IsHitTestVisible);

        // Auto section starts onscreen in the non-scrolling host, renders at 310x135, and remembers 310x135
        Assert.False(CssContainmentProperties.IsContentSkipped(autoSection));
        Assert.Equal(310, autoSection.ActualWidth, 3);
        Assert.Equal(135, autoSection.ActualHeight, 3);
        var remembered = CssContainmentProperties.GetRememberedRenderedSize(autoSection);
        Assert.NotNull(remembered);
        Assert.Equal(310, remembered!.Value.Width, 3);
        Assert.Equal(135, remembered.Value.Height, 3);

        // When autoSection transitions offscreen, it skips its content and retains the remembered 310x135 size
        // even if the child's dimensions change while skipped.
        focusableTarget.Width = 420;
        focusableTarget.Height = 220;
        CssContainmentProperties.SetOffscreenForAutoVisibility(autoSection, isOffscreen: true);
        Layout(host, 500, 500);

        Assert.True(CssContainmentProperties.IsContentSkipped(autoSection));
        Assert.Equal(310, autoSection.ActualWidth, 3);
        Assert.Equal(135, autoSection.ActualHeight, 3);
        Assert.Equal(1.0, focusableTarget.Opacity, 3);
        Assert.False(focusableTarget.IsHitTestVisible);

        // Bringing a descendant into view (or focusing it) automatically reveals the content-visibility: auto ancestor
        focusableTarget.BringIntoView();
        Layout(host, 500, 500);

        Assert.False(CssContainmentProperties.IsContentSkipped(autoSection));
        Assert.Equal(1.0, focusableTarget.Opacity, 3);
        Assert.True(focusableTarget.IsHitTestVisible);
        Assert.Equal(420, autoSection.ActualWidth, 3);
        Assert.Equal(220, autoSection.ActualHeight, 3);
    }

    [Fact]
    public void NativeUiPropertiesAccentColorZoomAppearanceColorSchemeAndUrlCursorRoundTrip()
    {
        var container = new StackPanel { Orientation = Orientation.Vertical };
        var control = new Button { Width = 120, Height = 40, BorderThickness = new Thickness(2) };
        // Clear local BorderThickness so appearance: none on ParentTemplate layer can strip default chrome border
        control.ClearValue(Control.BorderThicknessProperty);

        var scrollBox = new ScrollViewer { Width = 200, Height = 120 };
        var zoomedCard = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Border { Width = 100, Height = 60 },
        };

        container.Children.Add(control);
        container.Children.Add(scrollBox);
        container.Children.Add(zoomedCard);

        Css.SetStyle(
            container,
            "accent-color: #10b981; color-scheme: only dark; forced-color-adjust: preserve-parent-color; " +
            "print-color-adjust: exact; text-size-adjust: 125%;");

        Css.SetStyle(
            control,
            "appearance: none; resize: inline; cursor: url(\"reticle-crosshair.cur\") 12 8, pointer;");

        Css.SetStyle(scrollBox, "touch-action: pan-x pinch-zoom;");
        Css.SetStyle(zoomedCard, "zoom: 150%;");

        Layout(container, 500, 400);

        // 1. Inherited user preference & color adjustment properties on descendant control
        var accent = CssNativeUiProperties.GetAccentColor(control);
        Assert.False(accent.IsAuto);
        Assert.Equal(Color.FromRgb(0x10, 0xB9, 0x81), accent.Color);

        var scheme = CssNativeUiProperties.GetColorScheme(control);
        Assert.Equal(CssColorSchemeFlags.OnlyDark, scheme);
        Assert.Equal("dark", CssNativeUiProperties.ResolveEffectiveTheme(control, systemPrefersDark: false));

        var palette = CssNativeUiProperties.ResolveChromeSurfacePalette(control);
        Assert.Equal("dark", palette.EffectiveTheme);
        Assert.Equal(Color.FromRgb(0x10, 0xB9, 0x81), palette.AccentColor);

        Assert.Equal(CssForcedColorAdjust.PreserveParentColor, CssNativeUiProperties.GetForcedColorAdjust(control));
        Assert.Equal(CssPrintColorAdjust.Exact, CssNativeUiProperties.GetPrintColorAdjust(control));

        var textAdjust = CssNativeUiProperties.GetTextSizeAdjust(control);
        Assert.False(textAdjust.IsAuto);
        Assert.False(textAdjust.IsNone);
        Assert.Equal(1.25, textAdjust.Factor, 3);

        // 2. appearance: none strips default control BorderThickness
        Assert.Equal(CssAppearance.None, CssNativeUiProperties.GetAppearance(control));
        Assert.True(CssNativeUiProperties.IsNativeChromeSuppressed(control));
        Assert.Equal(new Thickness(0), control.BorderThickness);

        // 3. resize logical mapping
        Assert.Equal(CssResize.Inline, CssNativeUiProperties.GetResize(control));
        Assert.Equal(CssResize.Horizontal, CssNativeUiProperties.ResolvePhysicalResize(control));

        // 4. cursor: url(...) x y, fallback
        Assert.Equal("reticle-crosshair.cur", CssNativeUiProperties.GetCursorUrl(control));
        Assert.Equal(new Point(12, 8), CssNativeUiProperties.GetCursorHotspot(control));
        Assert.Equal(Cursors.Hand, CssNativeUiProperties.GetFallbackCursor(control));
        Assert.Equal(Cursors.Hand, control.Cursor);

        // 5. touch-action synchronization with ScrollViewer.PanningMode
        var touchAction = CssNativeUiProperties.GetTouchAction(scrollBox);
        Assert.Equal(CssTouchAction.PanX | CssTouchAction.PinchZoom, touchAction);
        Assert.Equal(PanningMode.HorizontalOnly, scrollBox.PanningMode);

        // 6. zoom scales layout DesiredSize via LayoutTransform
        Assert.Equal(1.5, CssNativeUiProperties.GetZoom(zoomedCard), 3);
        Assert.Equal(1.5, CssNativeUiProperties.GetEffectiveZoom(zoomedCard), 3);
        Assert.Equal(150, zoomedCard.DesiredSize.Width, 3);
        Assert.Equal(90, zoomedCard.DesiredSize.Height, 3);
    }

    [Fact]
    public void RenderR1R2R3ShowcaseScreenshots_NativeAndWithoutBorder_1xAnd2x()
    {
        var outputDirs = GetScreenshotDirectories();
        foreach (var dir in outputDirs)
            Directory.CreateDirectory(dir);

        foreach (var (windowStyle, modeName) in new[]
        {
            (WindowStyle.SingleBorderWindow, "native"),
            (WindowStyle.None, "withoutborder"),
        })
        {
            foreach (var (dpi, suffix) in new[] { (1.0, "1x"), (2.0, "2x") })
            {
                var visual = BuildR1R2R3ShowcaseVisual(windowStyle);
                VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi, dpi));
                const int width = 1080;
                const int height = 640;
                Layout(visual, width, height);

                int pixelWidth = (int)Math.Round(width * dpi);
                int pixelHeight = (int)Math.Round(height * dpi);
                using var target = _context.CreateOffscreenRenderTarget(pixelWidth, pixelHeight);
                target.SetDpi((float)(96.0 * dpi), (float)(96.0 * dpi));
                target.RequestReadback();
                target.BeginDraw();
                target.Clear(0, 0, 0, 0);
                using (var dc = new RenderTargetDrawingContext(target, _context))
                {
                    visual.Render(dc);
                    dc.Close();
                }
                target.EndDraw();

                var pixels = new byte[pixelWidth * pixelHeight * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, (uint)(pixelWidth * 4), out _, out _));
                int nonZero = pixels.Count(b => b != 0);
                Assert.True(nonZero > pixelWidth * pixelHeight, $"Expected rendered pixels for {modeName}-{suffix}");

                var bmp = BitmapSource.Create(
                    pixelWidth,
                    pixelHeight,
                    96.0 * dpi,
                    96.0 * dpi,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    pixelWidth * 4);
                var fileName = $"r1-r2-r3-scroll-contain-ui-{modeName}-{suffix}.png";
                foreach (var dir in outputDirs)
                {
                    var path = Path.Combine(dir, fileName);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var stream = File.Create(path);
                    encoder.Save(stream);
                    Assert.True(new FileInfo(path).Length > 4096);
                }
            }
        }
    }

    private static FrameworkElement BuildR1R2R3ShowcaseVisual(WindowStyle windowStyle)
    {
        bool isBorderless = windowStyle == WindowStyle.None;
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 18)),
        };
        Css.SetStyle(
            root,
            "display: block; width: 1080px; height: 640px; padding: 18px; color-scheme: only dark; accent-color: #10b981;");

        // Top Contextual Chrome Bar
        var headerBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 20, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 68)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 11, 16, 11),
        };
        Css.SetStyle(headerBar, "display: block; margin-bottom: 14px; border-radius: 10px;");

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var chromeBadge = new Border
        {
            Background = isBorderless
                ? new SolidColorBrush(Color.FromRgb(6, 78, 59))
                : new SolidColorBrush(Color.FromRgb(23, 37, 84)),
            BorderBrush = isBorderless
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                : new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3, 9, 3),
            Child = new TextBlock
            {
                Text = isBorderless ? "WITHOUTBORDER SURFACE" : "NATIVE WINDOW CHROME",
                Foreground = new SolidColorBrush(Color.FromRgb(236, 253, 245)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(chromeBadge, "border-radius: 6px;");
        headerRow.Children.Add(chromeBadge);

        headerRow.Children.Add(new TextBlock
        {
            Text = "Milestones R1 / R2 / R3 — Scroll Snap & Anchoring, CSS Containment & Native UI Chrome",
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
        });

        var statusBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(19, 28, 43)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = "SNAP: MANDATORY | CONTAIN: STRICT | ZOOM: 1.25x",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9.5,
            },
        };
        Css.SetStyle(statusBadge, "border-radius: 5px;");
        headerRow.Children.Add(statusBadge);
        headerBar.Child = headerRow;
        root.Children.Add(headerBar);

        // Main 3-Column Workbench Deck
        var mainDeck = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        // Column 1: R1 Scroll Container, Snapport & Scroll Anchoring
        var colR1 = BuildCardShell(
            "R1 // SCROLL SNAP & ANCHORING",
            "scroll-snap-type: y mandatory | overflow-anchor: auto",
            338,
            552);

        var r1Body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        r1Body.Children.Add(BuildMetricStrip("SNAPPORT PADDING", "20px 0px 10px 0px", Color.FromRgb(56, 189, 248)));
        r1Body.Children.Add(BuildMetricStrip("ANCHOR LOCK DELTA", "+70.0 px -> Y=170.0 px", Color.FromRgb(16, 185, 129)));
        r1Body.Children.Add(BuildMetricStrip("OVERSCROLL CHAINING", "contain / none (ISOLATED)", Color.FromRgb(245, 158, 11)));

        var snapTrackBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 15, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
        };
        Css.SetStyle(snapTrackBox, "border-radius: 8px; scroll-snap-type: y mandatory; scroll-padding-top: 16px;");

        var snapStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        snapStack.Children.Add(BuildSnapSlotCard("SLOT 01 // START ALIGN", "scroll-snap-align: start | margin-top: 12px", "OFFSET 148.0 px", true));
        snapStack.Children.Add(BuildSnapSlotCard("SLOT 02 // ALWAYS STOP", "scroll-snap-stop: always | center", "OFFSET 345.0 px", false));
        snapStack.Children.Add(BuildSnapSlotCard("SLOT 03 // PROXIMITY", "scroll-snap-align: end | margin-bottom: 8px", "OFFSET 512.0 px", false));
        snapTrackBox.Child = snapStack;
        r1Body.Children.Add(snapTrackBox);

        colR1.Item2.Children.Add(r1Body);
        mainDeck.Children.Add(colR1.Item1);

        // Column 2: R2 Containment & On-Demand Content-Visibility
        var colR2 = BuildCardShell(
            "R2 // CONTAINMENT & CONTENT-VISIBILITY",
            "contain: strict | content-visibility: auto / hidden",
            338,
            552);

        var r2Body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        r2Body.Children.Add(BuildMetricStrip("CONTAINMENT FLAGS", "size | layout | style | paint", Color.FromRgb(16, 185, 129)));
        r2Body.Children.Add(BuildMetricStrip("INTRINSIC PLACEHOLDER", "auto 240px 110px (REMEMBERED)", Color.FromRgb(56, 189, 248)));
        r2Body.Children.Add(BuildMetricStrip("COUNTER ISOLATION", "sec=1 (OUTER) | sec=5 (SCOPED)", Color.FromRgb(168, 85, 247)));

        var containDemoBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(12, 19, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
        };
        Css.SetStyle(containDemoBox, "border-radius: 8px; contain: content;");

        var containDemoStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        containDemoStack.Children.Add(new TextBlock
        {
            Text = "CONTAINED SUBTREE BOUNDARY (ClipToBounds + Fixed CB)",
            Foreground = new SolidColorBrush(Color.FromRgb(209, 250, 229)),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
        });
        containDemoStack.Children.Add(new TextBlock
        {
            Text = "EstablishesFixedContainingBlock = true | Measure isolated",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10,
        });
        containDemoBox.Child = containDemoStack;
        r2Body.Children.Add(containDemoBox);

        var cvAutoBox = BuildSnapSlotCard(
            "CONTENT-VISIBILITY: AUTO (REVEALED)",
            "Onscreen / Focus-Revealed -> Real Subtree 310x135 px",
            "ACTIVE",
            true);
        r2Body.Children.Add(cvAutoBox);

        var cvHiddenBox = BuildSnapSlotCard(
            "CONTENT-VISIBILITY: HIDDEN (SKIPPED)",
            "Subtree suppressed -> Intrinsic Box 210x85 px",
            "SKIPPED",
            false);
        r2Body.Children.Add(cvHiddenBox);

        colR2.Item2.Children.Add(r2Body);
        mainDeck.Children.Add(colR2.Item1);

        // Column 3: R3 Native UI Chrome, Accent Color, Zoom & Cursor URL
        var colR3 = BuildCardShell(
            "R3 // NATIVE UI & USER SETTINGS",
            "appearance | accent-color | color-scheme | zoom | cursor",
            340,
            552);

        var r3Body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        r3Body.Children.Add(BuildMetricStrip("COLOR-SCHEME / ACCENT", "only dark | #10B981 EMERALD", Color.FromRgb(16, 185, 129)));
        r3Body.Children.Add(BuildMetricStrip("APPEARANCE / RESIZE", "appearance: none | resize: inline", Color.FromRgb(56, 189, 248)));
        r3Body.Children.Add(BuildMetricStrip("CURSOR FALLBACK CHAIN", "url('reticle.cur') 12 8, pointer", Color.FromRgb(245, 158, 11)));

        // Bespoke Control Strip with appearance: none + accent-color
        var accentCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 27)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
        };
        Css.SetStyle(accentCard, "border-radius: 8px; appearance: none; cursor: url(\"reticle.cur\") 12 8, pointer;");

        var accentStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        accentStack.Children.Add(new TextBlock
        {
            Text = "BESPOKE CONTROL SURFACE (appearance: none)",
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
        });

        var progressTrack = new Border
        {
            Height = 10,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Child = new Border
            {
                Width = 195,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
            },
        };
        Css.SetStyle(progressTrack, "border-radius: 5px;");
        accentStack.Children.Add(progressTrack);

        accentStack.Children.Add(new TextBlock
        {
            Text = "touch-action: pan-x pinch-zoom -> PanningMode.HorizontalOnly",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 9,
        });
        accentCard.Child = accentStack;
        r3Body.Children.Add(accentCard);

        // Zoom Scaling Preview Card (zoom: 125%)
        var zoomOuter = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Css.SetStyle(zoomOuter, "border-radius: 8px; zoom: 125%;");
        zoomOuter.Child = new TextBlock
        {
            Text = "ZOOM: 125% LAYOUT TRANSFORM SCALE",
            Foreground = new SolidColorBrush(Color.FromRgb(224, 242, 254)),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        };
        r3Body.Children.Add(zoomOuter);

        r3Body.Children.Add(BuildSnapSlotCard(
            "FORCED & PRINT COLOR ADJUST",
            "forced-color-adjust: preserve-parent-color | print: exact",
            "text-size: 125%",
            true));

        colR3.Item2.Children.Add(r3Body);
        mainDeck.Children.Add(colR3.Item1);

        root.Children.Add(mainDeck);
        return root;
    }

    private static (Border Shell, StackPanel ContentHost) BuildCardShell(
        string title,
        string subtitle,
        int width,
        int height)
    {
        var shell = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromRgb(15, 20, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 68)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
        };
        Css.SetStyle(shell, "border-radius: 10px;");

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        var headerBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 27, 41)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 79)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
        };
        Css.SetStyle(headerBox, "border-radius: 7px;");

        var headerStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
        headerStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10,
        });
        headerBox.Child = headerStack;
        stack.Children.Add(headerBox);

        shell.Child = stack;
        return (shell, stack);
    }

    private static Border BuildMetricStrip(string label, string value, Color accentColor)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            Background = new SolidColorBrush(accentColor),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Css.SetStyle(dot, "border-radius: 4px;");
        row.Children.Add(dot);

        row.Children.Add(new TextBlock
        {
            Text = label + ":",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
        });

        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            FontSize = 9.5,
        });

        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 16, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7),
            Child = row,
        };
        Css.SetStyle(container, "border-radius: 6px;");
        return container;
    }

    private static Border BuildSnapSlotCard(string title, string detail, string badgeText, bool active)
    {
        var outer = new Border
        {
            Background = active
                ? new SolidColorBrush(Color.FromRgb(13, 31, 38))
                : new SolidColorBrush(Color.FromRgb(13, 19, 30)),
            BorderBrush = active
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                : new SolidColorBrush(Color.FromRgb(38, 49, 68)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
        };
        Css.SetStyle(outer, "border-radius: 7px; scroll-snap-align: start;");

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        topRow.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
        });

        var pill = new Border
        {
            Background = active
                ? new SolidColorBrush(Color.FromRgb(6, 78, 59))
                : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = badgeText,
                Foreground = active
                    ? new SolidColorBrush(Color.FromRgb(167, 243, 208))
                    : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(pill, "border-radius: 4px;");
        topRow.Children.Add(pill);

        stack.Children.Add(topRow);
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10,
        });

        outer.Child = stack;
        return outer;
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (int pass = 0; pass < 5; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }
    }

    private static string[] GetScreenshotDirectories()
    {
        var list = new List<string>();
        if (Environment.GetEnvironmentVariable("JALIUM_R1R2R3_SCREENSHOT_DIR") is { Length: > 0 } envDir)
            list.Add(envDir);

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Jalium.UI.sln")))
            current = current.Parent;
        var repoRoot = current?.FullName ?? "/Users/jal/Documents/Repos/Jalium.UI";
        list.Add(Path.Combine(repoRoot, "artifacts", "screenshots", "css"));

        const string acceptanceRoot = "/Volumes/My PSSD/.codex-css-acceptance";
        if (Directory.Exists(acceptanceRoot))
            list.Add(Path.Combine(acceptanceRoot, "css-r1-r2-r3-scroll-contain-ui-20261026", "screenshots"));

        return list.Distinct(StringComparer.Ordinal).ToArray();
    }
}
