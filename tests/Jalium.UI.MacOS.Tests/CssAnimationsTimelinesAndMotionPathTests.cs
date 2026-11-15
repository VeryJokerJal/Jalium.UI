using System.Diagnostics;
using System.Globalization;
using System.IO;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssAnimationsTimelinesAndMotionPathTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

    public CssAnimationsTimelinesAndMotionPathTests()
    {
        EnsureM1PropertiesRegistered();
    }

    public void Dispose() => _context.Dispose();

    private static void EnsureM1PropertiesRegistered()
    {
        CssScrollAndViewTimelines.Register();
        CssMotionPath.Register();
        CssViewTransitions.Register();
    }

    [Fact]
    public void ScrollTimelineAndViewTimelineDriveKeyframeProgressDeterministically()
    {
        EnsureM1PropertiesRegistered();

        foreach (var prop in new[]
        {
            "animation-timeline",
            "scroll-timeline",
            "scroll-timeline-name",
            "scroll-timeline-axis",
            "view-timeline",
            "view-timeline-name",
            "view-timeline-axis",
            "view-timeline-inset",
            "timeline-scope",
            "animation-range",
            "animation-range-start",
            "animation-range-end",
        })
        {
            Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty(prop).Support);
        }

        // 1. Anonymous scroll(nearest block) timeline + animation-range: 20% 80%
        var host = new StackPanel { Width = 300, Height = 260 };
        Css.SetStyle(host, "timeline-scope: --main-scroller;");

        var externalProgressBadge = new Border { Height = 20 };
        Css.SetClass(externalProgressBadge, "scoped-progress");
        host.Children.Add(externalProgressBadge);

        var scroller = new ScrollViewer
        {
            Width = 300,
            Height = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Css.SetClass(scroller, "main-scroll");

        var content = new StackPanel { Width = 300, Height = 600 };
        var scrollDrivenBox = new Border { Height = 40 };
        Css.SetClass(scrollDrivenBox, "scroll-box");
        var rangedBox = new Border { Height = 40 };
        Css.SetClass(rangedBox, "ranged-box");

        var spacerBefore = new Border { Width = 300, Height = 220 };
        var viewSubject = new Border { Height = 100 };
        Css.SetClass(viewSubject, "view-subject");
        var spacerAfter = new Border { Width = 300, Height = 200 };

        content.Children.Add(scrollDrivenBox);
        content.Children.Add(rangedBox);
        content.Children.Add(spacerBefore);
        content.Children.Add(viewSubject);
        content.Children.Add(spacerAfter);
        scroller.Content = content;
        host.Children.Add(scroller);

        var sheet = CssStyleSheet.Parse("""
            @keyframes expand-width {
                from { width: 40px; opacity: 0.2; }
                to { width: 240px; opacity: 1.0; }
            }
            .main-scroll {
                scroll-timeline: --main-scroller block;
            }
            .scroll-box {
                animation: expand-width 1s linear both;
                animation-timeline: scroll(nearest block);
            }
            .ranged-box {
                animation: expand-width 1s linear both;
                animation-timeline: scroll(nearest block);
                animation-range: 20% 80%;
            }
            .scoped-progress {
                animation: expand-width 1s linear both;
                animation-timeline: --main-scroller;
            }
            .view-subject {
                animation: expand-width 1s linear both;
                animation-timeline: view(block);
                animation-range: cover 0% cover 100%;
            }
            """);
        Assert.Empty(sheet.Diagnostics);
        Css.SetStyleSheets(host, new CssStyleSheetCollection { sheet });

        Layout(host, 300, 260);

        // Total content height is 40 + 40 + 220 + 100 + 200 = 600px; viewport is 200px -> ScrollableHeight = 400px.
        Assert.Equal(400.0, scroller.ScrollableHeight, 1);

        // At scrollOffset = 0 (0%):
        scroller.ScrollToVerticalOffset(0);
        Layout(host, 300, 260);
        Assert.Equal(40.0, scrollDrivenBox.Width, 2);
        Assert.Equal(40.0, externalProgressBadge.Width, 2);
        Assert.Equal(40.0, rangedBox.Width, 2);

        // At scrollOffset = 200 (50% of 400px):
        // - scrollDrivenBox & externalProgressBadge are at 50% -> width = 40 + 200 * 0.5 = 140px, opacity = 0.6
        // - rangedBox (20%..80% = 80px..320px): (200 - 80) / 240 = 0.5 -> width = 140px
        scroller.ScrollToVerticalOffset(200);
        Layout(host, 300, 260);
        Assert.Equal(140.0, scrollDrivenBox.Width, 2);
        Assert.Equal(0.6, scrollDrivenBox.Opacity, 2);
        Assert.Equal(140.0, externalProgressBadge.Width, 2);
        Assert.Equal(140.0, rangedBox.Width, 2);

        // At scrollOffset = 80 (20% of 400px):
        // - scrollDrivenBox is at 20% -> width = 40 + 200 * 0.2 = 80px
        // - rangedBox is at start of 20%..80% -> progress = 0.0 -> width = 40px
        scroller.ScrollToVerticalOffset(80);
        Layout(host, 300, 260);
        Assert.Equal(80.0, scrollDrivenBox.Width, 2);
        Assert.Equal(40.0, rangedBox.Width, 2);

        // View timeline on viewSubject (P0 = 300px, height S = 100px, viewport V = 200px):
        // cover 0% is at P0 - V = 300 - 200 = 100px; cover 100% is at P0 + S = 300 + 100 = 400px (span = 300px).
        // At scrollOffset = 250px -> view progress = (250 - 100) / 300 = 0.5 -> width = 140px!
        scroller.ScrollToVerticalOffset(250);
        Layout(host, 300, 260);
        Assert.True(CssScrollAndViewTimelines.TryResolveProgress(viewSubject, out var viewProgress));
        Assert.Equal(0.5, viewProgress, 2);
        Assert.Equal(140.0, viewSubject.Width, 2);

        // At scrollOffset = 400 (100%): all reach 240px!
        scroller.ScrollToVerticalOffset(400);
        Layout(host, 300, 260);
        Assert.Equal(240.0, scrollDrivenBox.Width, 2);
        Assert.Equal(240.0, externalProgressBadge.Width, 2);
        Assert.Equal(240.0, rangedBox.Width, 2);
        Assert.Equal(240.0, viewSubject.Width, 2);
    }

    [Fact]
    public void MotionPathComputesPositionAndTangentAngleAcrossPathRayAndShapes()
    {
        EnsureM1PropertiesRegistered();

        foreach (var prop in new[]
        {
            "offset",
            "offset-path",
            "offset-distance",
            "offset-rotate",
            "offset-anchor",
            "offset-position",
        })
        {
            Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty(prop).Support);
        }

        var container = new Grid { Width = 200, Height = 200 };
        var actor = new Border { Width = 20, Height = 10 };
        container.Children.Add(actor);

        // 1. SVG path("M 0 0 L 100 0 L 100 100") — total length 200px
        Css.SetStyle(actor, """
            offset-path: path("M 0 0 L 100 0 L 100 100");
            offset-distance: 25%;
            offset-rotate: auto;
            offset-anchor: 0px 0px;
            """);
        Layout(container, 200, 200);
        var pose25 = CssMotionPath.ResolveTransform(actor, new Size(20, 10), new Size(200, 200));
        Assert.Equal(50.0, pose25.X, 2);
        Assert.Equal(0.0, pose25.Y, 2);
        Assert.Equal(0.0, pose25.AngleDegrees, 2);

        // At 75% (150px along path -> second segment at (100, 50), tangent 90deg) with offset-rotate: reverse (+180deg => 270deg)
        Css.SetStyle(actor, """
            offset-path: path("M 0 0 L 100 0 L 100 100");
            offset-distance: 75%;
            offset-rotate: reverse;
            offset-anchor: 0px 0px;
            """);
        Layout(container, 200, 200);
        var pose75 = CssMotionPath.ResolveTransform(actor, new Size(20, 10), new Size(200, 200));
        Assert.Equal(100.0, pose75.X, 2);
        Assert.Equal(50.0, pose75.Y, 2);
        Assert.Equal(270.0, pose75.AngleDegrees, 2);

        // 2. ray(90deg closest-side) from center (100, 100) of 200x200 box -> closest-side radius = 100px, pointing +X (tangent 0deg)
        Css.SetStyle(actor, """
            offset-position: 50% 50%;
            offset-path: ray(90deg closest-side);
            offset-distance: 60%;
            offset-rotate: auto;
            """);
        Layout(container, 200, 200);
        var rayPose = CssMotionPath.ResolveTransform(actor, new Size(20, 10), new Size(200, 200));
        Assert.Equal(160.0, rayPose.X, 2);
        Assert.Equal(100.0, rayPose.Y, 2);
        Assert.Equal(0.0, rayPose.AngleDegrees, 2);

        // 3. Motion Path 1 equivalent circles start at the rightmost point.
        // At 25% -> bottom (100, 150), clockwise tangent pointing left (180deg).
        Css.SetStyle(actor, """
            offset-path: circle(50px at 100px 100px);
            offset-distance: 25%;
            offset-rotate: auto;
            """);
        Layout(container, 200, 200);
        var circlePose = CssMotionPath.ResolveTransform(actor, new Size(20, 10), new Size(200, 200));
        Assert.Equal(100.0, circlePose.X, 2);
        Assert.Equal(150.0, circlePose.Y, 2);
        Assert.Equal(180.0, circlePose.AngleDegrees, 2);

        // 4. Keyframe animation on offset-distance and offset-rotate
        var sheet = CssStyleSheet.Parse("""
            @keyframes move-along {
                from { offset-distance: 0%; offset-rotate: auto 0deg; }
                to { offset-distance: 100%; offset-rotate: auto 90deg; }
            }
            """);
        Css.SetStyleSheets(container, new CssStyleSheetCollection { sheet });
        Css.SetStyle(actor, """
            offset-path: path("M 0 0 L 200 0");
            animation: move-along 100s linear -50s both paused;
            """);
        Layout(container, 200, 200);
        var animatedPose = CssMotionPath.ResolveTransform(actor, new Size(20, 10), new Size(200, 200));
        Assert.Equal(100.0, animatedPose.X, 2);
        Assert.Equal(0.0, animatedPose.Y, 2);
        Assert.Equal(45.0, animatedPose.AngleDegrees, 2);
    }

    [Fact]
    public void NonVisualDocumentNodesAndDiscreteAllowDiscreteTransitionsInterpolateAccurately()
    {
        EnsureM1PropertiesRegistered();

        var run = new Run("Animated Document Run");
        var span = new Span(run);
        var paragraph = new Paragraph(span);
        var section = new Section(paragraph);
        var document = new FlowDocument(section)
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 16,
            PagePadding = new Thickness(8),
        };
        var viewer = new FlowDocumentScrollViewer
        {
            Document = document,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        // 1. Initial style on non-visual Run and Paragraph
        Css.SetStyle(run, "font-size: 16px; letter-spacing: 0px; font-style: normal; transition: font-size 10s linear, letter-spacing 10s linear, font-style 10s linear allow-discrete;");
        Css.SetStyle(paragraph, "margin-top: 4px; transition: margin-top 10s linear;");
        Layout(viewer, 360, 160);

        Assert.Equal(16.0, run.FontSize, 3);
        Assert.False(CssTransitions.HasActiveTransition(run, TextElement.FontSizeProperty));

        // 2. Mutate style on non-visual Run and Paragraph -> starts automatic transitions on FrameworkContentElement!
        Css.SetStyle(run, "font-size: 36px; letter-spacing: 12px; font-style: italic; transition: font-size 10s linear, letter-spacing 10s linear, font-style 10s linear allow-discrete;");
        Css.SetStyle(paragraph, "margin-top: 24px; transition: margin-top 10s linear;");
        Evaluate(document);

        Assert.True(CssTransitions.HasActiveTransition(run, TextElement.FontSizeProperty));
        Assert.True(CssTransitions.HasActiveTransition(run, CssFlowProperties.LetterSpacingProperty));
        Assert.True(CssTransitions.HasActiveTransition(run, TextElement.FontStyleProperty));

        // Sample at 25%: font-size = 16 + 20 * 0.25 = 21px, letter-spacing = 3px, discrete font-style is still Normal (< 0.5)
        Assert.True(CssTransitions.SampleActiveTransitions(run, 0.25));
        Assert.Equal(21.0, run.FontSize, 3);
        Assert.Equal(3.0, CssTextSpacing.Letter(run), 3);
        Assert.Equal(FontStyles.Normal, run.FontStyle);

        // Sample at 75%: font-size = 16 + 20 * 0.75 = 31px, letter-spacing = 9px, discrete font-style flips to Italic (>= 0.5)
        Assert.True(CssTransitions.SampleActiveTransitions(run, 0.75));
        Assert.Equal(31.0, run.FontSize, 3);
        Assert.Equal(9.0, CssTextSpacing.Letter(run), 3);
        Assert.Equal(FontStyles.Italic, run.FontStyle);

        // Public tree integrity preserved
        Assert.Same(span, run.Parent);
        Assert.Same(paragraph, span.Parent);
        Assert.Same(section, paragraph.Parent);

        // 3. UIElement discrete transition with allow-discrete vs normal
        var discreteBox = new Border();
        Css.SetStyle(discreteBox, "visibility: visible; transition: visibility 2s linear allow-discrete;");
        Layout(discreteBox, 100, 40);
        var trData = Assert.IsType<CssTransitionData>(discreteBox.GetValue(CssTransitions.DataProperty));
        var visTr = trData.Find(discreteBox, CssDisplayProperties.VisibilityProperty)!;
        Assert.NotNull(visTr);
        Assert.True(visTr.AllowDiscrete);

        // Normal behavior returns null for discrete visibility, whereas AllowDiscrete returns CssDiscreteAnimation
        Assert.Null(CssTransitions.Create(discreteBox, CssDisplayProperties.VisibilityProperty, Visibility.Visible, Visibility.Hidden, visTr with { AllowDiscrete = false }));
        var discreteAnim = Assert.IsType<CssDiscreteAnimation>(
            CssTransitions.Create(discreteBox, CssDisplayProperties.VisibilityProperty, Visibility.Visible, Visibility.Hidden, visTr));
        var clock = discreteAnim.CreateClock();
        clock.SetProgressForSampling(0.49);
        Assert.Equal(Visibility.Visible, discreteAnim.GetCurrentValue(Visibility.Visible, Visibility.Hidden, clock));
        clock.SetProgressForSampling(0.50);
        Assert.Equal(Visibility.Hidden, discreteAnim.GetCurrentValue(Visibility.Visible, Visibility.Hidden, clock));
    }

    [Fact]
    public void AnimationCompositionAddAndAccumulateCombineWithUnderlyingValues()
    {
        EnsureM1PropertiesRegistered();

        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("animation-composition").Support);
        Assert.True(CssAuthoring.IsValueSupported("animation-composition", "replace, add, accumulate"));

        var host = new StackPanel { Width = 300, Height = 200 };
        var boxReplace = new Border { Height = 24 };
        var boxAdd = new Border { Height = 24 };
        var boxAccumulate = new Border { Height = 24 };
        var boxTransformAdd = new Border { Width = 40, Height = 24 };
        var boxTransformAccumulate = new Border { Width = 40, Height = 24 };

        host.Children.Add(boxReplace);
        host.Children.Add(boxAdd);
        host.Children.Add(boxAccumulate);
        host.Children.Add(boxTransformAdd);
        host.Children.Add(boxTransformAccumulate);

        var sheet = CssStyleSheet.Parse("""
            @keyframes delta-width {
                from { width: 20px; }
                to { width: 60px; }
            }
            @keyframes delta-scale {
                from { transform: scale(2, 2); }
                to { transform: scale(3, 3); }
            }
            """);
        Css.SetStyleSheets(host, new CssStyleSheetCollection { sheet });

        // Underlying width is 100px; keyframe at 50% samples 40px (and end value is 60px).
        Css.SetStyle(boxReplace, "width: 100px; animation: delta-width 10s linear -5s both paused; animation-composition: replace;");
        Css.SetStyle(boxAdd, "width: 100px; animation: delta-width 10s linear -5s both paused; animation-composition: add;");
        // At elapsed 15s (iteration index 1, 50% into second iteration) with iteration-count: 3:
        // accumulate adds underlying (100) + current sample (40) + 1 * iterationEnd (60) = 200px!
        Css.SetStyle(boxAccumulate, "width: 100px; animation: delta-width 10s linear -15s 3 both paused; animation-composition: accumulate;");

        // Transform composition at 50% (sampled scale is 2.5) on top of underlying scale(2, 2):
        // - add multiplies: 2.0 * 2.5 = 5.0
        // - accumulate adds deltas from 1.0: 2.0 + (2.5 - 1.0) = 3.5
        Css.SetStyle(boxTransformAdd, "transform: scale(2, 2); animation: delta-scale 10s linear -5s both paused; animation-composition: add;");
        Css.SetStyle(boxTransformAccumulate, "transform: scale(2, 2); animation: delta-scale 10s linear -5s both paused; animation-composition: accumulate;");

        Layout(host, 300, 200);

        Assert.Equal(40.0, boxReplace.Width, 2);
        Assert.Equal(140.0, boxAdd.Width, 2);
        Assert.Equal(200.0, boxAccumulate.Width, 2);

        Assert.NotNull(boxTransformAdd.RenderTransform);
        Assert.NotNull(boxTransformAccumulate.RenderTransform);
        Assert.Equal(5.0, boxTransformAdd.RenderTransform!.Value.M11, 2);
        Assert.Equal(3.5, boxTransformAccumulate.RenderTransform!.Value.M11, 2);
    }

    [Fact]
    public void ViewTransitionHostCapturesNamedElementsAndInterpolatesSnapshotBounds()
    {
        EnsureM1PropertiesRegistered();

        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("view-transition-name").Support);
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("view-transition-class").Support);

        var root = new Grid { Width = 400, Height = 240 };
        Css.SetStyle(root, "position: relative;");

        var heroCard = new Border { Background = Brushes.CornflowerBlue };
        Css.SetStyle(heroCard, """
            position: absolute;
            left: 20px;
            top: 30px;
            width: 100px;
            height: 60px;
            opacity: 0.4;
            view-transition-name: hero-card;
            view-transition-class: card-morph elevated;
            """);
        root.Children.Add(heroCard);

        Layout(root, 400, 240);
        Assert.Equal("hero-card", CssViewTransitions.GetViewTransitionName(heroCard));
        Assert.Equal("card-morph elevated", CssViewTransitions.GetViewTransitionClass(heroCard));

        var transition = CssViewTransitionHost.StartViewTransition(root, () =>
        {
            Css.SetStyle(heroCard, """
                position: absolute;
                left: 180px;
                top: 90px;
                width: 180px;
                height: 100px;
                opacity: 1.0;
                view-transition-name: hero-card;
                view-transition-class: card-morph elevated;
                """);
        });

        Assert.True(transition.IsActive);
        Assert.Same(transition, CssViewTransitionHost.GetActiveTransition(root));

        var participant = transition.GetParticipant("hero-card");
        Assert.NotNull(participant);
        Assert.Equal(new Rect(20, 30, 100, 60), participant!.OldBounds);
        Assert.Equal(new Rect(180, 90, 180, 100), participant.NewBounds);
        Assert.Equal(0.4, participant.OldOpacity, 3);
        Assert.Equal(1.0, participant.NewOpacity, 3);

        // Seek to 50%: bounds = (100, 60, 140, 80), opacity = 0.7
        transition.Seek(0.5);
        Assert.Equal(new Rect(100, 60, 140, 80), participant.CurrentBounds);
        Assert.Equal(0.7, participant.CurrentOpacity, 3);
        Assert.Equal(0.7, heroCard.Opacity, 3);

        // Complete transition -> restores live state and marks completed
        transition.Complete();
        Assert.False(transition.IsActive);
        Assert.True(transition.IsCompleted);
        Assert.Null(CssViewTransitionHost.GetActiveTransition(root));
        Assert.Equal(1.0, heroCard.Opacity, 3);
    }

    [Fact]
    public void RenderM1ShowcaseScreenshots_NativeAndWithoutBorder_1xAnd2x()
    {
        EnsureM1PropertiesRegistered();

        var outDir = Environment.GetEnvironmentVariable("JALIUM_M1_SCREENSHOT_DIR")
            ?? "/Volumes/My PSSD/.codex-css-acceptance/css-m1-animations-timelines-motion-20261026/screenshots";
        Directory.CreateDirectory(outDir);

        foreach (var (windowStyle, modeName) in new[]
        {
            (WindowStyle.SingleBorderWindow, "native"),
            (WindowStyle.None, "withoutborder"),
        })
        {
            foreach (var (dpi, suffix) in new[] { (1.0, "1x"), (2.0, "2x") })
            {
                var visual = BuildM1ShowcaseVisual(windowStyle);
                VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi, dpi));
                const int width = 920;
                const int height = 560;
                Layout(visual, width, height);

                var pixelWidth = (int)Math.Round(width * dpi);
                var pixelHeight = (int)Math.Round(height * dpi);
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
                var nonZero = pixels.Count(b => b != 0);
                Assert.True(nonZero > pixelWidth * pixelHeight, $"Expected rendered content for {modeName}-{suffix}");

                var path = Path.Combine(outDir, $"m1-animations-timelines-motion-{modeName}-{suffix}.png");
                var bmp = BitmapSource.Create(pixelWidth, pixelHeight, 96.0 * dpi, 96.0 * dpi, PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(path);
                encoder.Save(stream);
                Assert.True(new FileInfo(path).Length > 1024);
            }
        }
    }

    private static FrameworkElement BuildM1ShowcaseVisual(WindowStyle windowStyle)
    {
        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 15, 25)),
        };
        Css.SetStyle(root, "display: block; width: 920px; height: 560px; padding: 18px; position: relative;");

        // Top Command Chrome Bar
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 11, 16, 11),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Children =
                {
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 3, 8, 3),
                        CornerRadius = new CornerRadius(5),
                        Child = new TextBlock
                        {
                            Text = "M1 TIMELINE STUDIO",
                            Foreground = new SolidColorBrush(Color.FromRgb(224, 242, 254)),
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                        },
                    },
                    new TextBlock
                    {
                        Text = $"Scroll & View Timelines / Motion Path / View Transitions  |  Chrome: {(windowStyle == WindowStyle.None ? "WithoutBorder" : "Native Window")}",
                        Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        Css.SetStyle(header, "display: block; margin-bottom: 14px; border-radius: 10px;");
        root.Children.Add(header);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        // Panel 1: Scroll-Driven & View Progress Timelines
        var panel1 = new StackPanel
        {
            Width = 285,
            Height = 462,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            Spacing = 10,
        };
        Css.SetStyle(panel1, "border: 1px solid #334155; border-radius: 10px; padding: 12px;");

        panel1.Children.Add(new TextBlock
        {
            Text = "Scroll & View Progress Timelines",
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        panel1.Children.Add(new TextBlock
        {
            Text = "scroll(nearest block) + view(block) + animation-range",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10.5,
        });

        var sampleOffsets = new (string Label, string RangeDesc, double Progress, Color BarColor)[]
        {
            ("scroll(nearest block)", "offset: 25% (100px / 400px)", 0.25, Color.FromRgb(56, 189, 248)),
            ("scroll(nearest block)", "offset: 60% (240px / 400px)", 0.60, Color.FromRgb(14, 165, 233)),
            ("view(block) entry", "animation-range: entry 0% 100%", 0.80, Color.FromRgb(16, 185, 129)),
            ("view(block) contain", "animation-range: contain 10% 90%", 0.50, Color.FromRgb(245, 158, 11)),
            ("--main-scroller (scoped)", "timeline-scope: --main-scroller", 0.92, Color.FromRgb(244, 63, 94)),
        };

        foreach (var (label, desc, prog, color) in sampleOffsets)
        {
            var card = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Spacing = 5,
            };
            Css.SetStyle(card, "border: 1px solid #475569; border-radius: 7px; padding: 8px;");
            card.Children.Add(new TextBlock
            {
                Text = $"{label}  [{(int)(prog * 100)}%]",
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            });
            card.Children.Add(new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 10,
            });
            var track = new Border
            {
                Width = 214,
                Height = 8,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new Border
                {
                    Width = Math.Round(214 * prog),
                    Height = 8,
                    Background = new SolidColorBrush(color),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                },
            };
            card.Children.Add(track);
            panel1.Children.Add(card);
        }

        row.Children.Add(panel1);

        // Panel 2: CSS Motion Path Stage & Keyframe Composition
        var panel2 = new StackPanel
        {
            Width = 285,
            Height = 462,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            Spacing = 10,
        };
        Css.SetStyle(panel2, "border: 1px solid #334155; border-radius: 10px; padding: 12px;");

        panel2.Children.Add(new TextBlock
        {
            Text = "Motion Path & Composition",
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        panel2.Children.Add(new TextBlock
        {
            Text = "offset-path: path() / ray() / circle() + add / accumulate",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10,
        });

        var motionStage = new Grid
        {
            Width = 259,
            Height = 190,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
        };
        Css.SetStyle(motionStage, "position: relative; border: 1px solid #38bdf8; border-radius: 8px;");

        var orbitGuide = new Border
        {
            Width = 120,
            Height = 120,
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(60),
        };
        Css.SetStyle(orbitGuide, "position: absolute; left: 56px; top: 35px;");
        motionStage.Children.Add(orbitGuide);

        foreach (var (pct, badgeText, badgeColor) in new[]
        {
            ("0%", "0% (0deg)", Color.FromRgb(56, 189, 248)),
            ("25%", "25% (90deg)", Color.FromRgb(16, 185, 129)),
            ("50%", "50% (180deg)", Color.FromRgb(245, 158, 11)),
            ("75%", "75% (270deg)", Color.FromRgb(244, 63, 94)),
        })
        {
            var marker = new Border
            {
                Width = 68,
                Height = 22,
                Background = new SolidColorBrush(badgeColor),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock
                {
                    Text = badgeText,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                },
            };
            Css.SetStyle(marker, $"position: absolute; left: 0px; top: 0px; offset-path: circle(60px at 116px 95px); offset-distance: {pct}; offset-rotate: 0deg;");
            motionStage.Children.Add(marker);
        }

        panel2.Children.Add(motionStage);

        // Composition readout bars (replace vs add vs accumulate)
        var compBox = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Spacing = 6,
        };
        Css.SetStyle(compBox, "border: 1px solid #475569; border-radius: 8px; padding: 10px;");
        compBox.Children.Add(new TextBlock
        {
            Text = "animation-composition (Base = 100px, Sample = +40px)",
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        });

        foreach (var (modeLabel, widthPx, color) in new[]
        {
            ("replace -> 40px", 40.0, Color.FromRgb(148, 163, 184)),
            ("add -> 140px", 140.0, Color.FromRgb(56, 189, 248)),
            ("accumulate (iter 2) -> 200px", 200.0, Color.FromRgb(16, 185, 129)),
        })
        {
            compBox.Children.Add(new TextBlock
            {
                Text = modeLabel,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 10,
            });
            compBox.Children.Add(new Border
            {
                Width = widthPx,
                Height = 10,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }
        panel2.Children.Add(compBox);
        row.Children.Add(panel2);

        // Panel 3: Document Node Transitions & View Transition Snapshots
        var panel3 = new StackPanel
        {
            Width = 286,
            Height = 462,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            Spacing = 10,
        };
        Css.SetStyle(panel3, "border: 1px solid #334155; border-radius: 10px; padding: 12px;");

        panel3.Children.Add(new TextBlock
        {
            Text = "Document & View Transitions",
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        panel3.Children.Add(new TextBlock
        {
            Text = "Run/Span/Paragraph transitions + ViewTransitionHost",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 10.5,
        });

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Helvetica"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            PagePadding = new Thickness(8),
        };
        var pDoc = new Paragraph();
        var r1 = new Run("Run @25%: 18px Normal\n") { FontSize = 18, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)) };
        var r2 = new Run("Run @50% (allow-discrete flip): 22px Italic\n") { FontSize = 20, FontStyle = FontStyles.Italic, Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)) };
        var r3 = new Run("Run @100%: 26px Bold Settled") { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)) };
        pDoc.Inlines.Add(r1);
        pDoc.Inlines.Add(r2);
        pDoc.Inlines.Add(r3);
        doc.Blocks.Add(pDoc);

        var docViewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            Width = 260,
            Height = 148,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Css.SetStyle(docViewer, "border: 1px solid #475569; border-radius: 8px;");
        panel3.Children.Add(docViewer);

        // View Transition interpolation stage showing Old -> 50% Seek -> New bounds
        var vtStage = new Grid
        {
            Width = 260,
            Height = 216,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
        };
        Css.SetStyle(vtStage, "position: relative; border: 1px solid #475569; border-radius: 8px; padding: 8px;");

        var vtOldGhost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(60, 56, 189, 248)),
            Padding = new Thickness(6, 4, 6, 4),
            Child = new TextBlock
            {
                Text = "Old Snapshot (t=0.0)\n[14, 18, 92x44]",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9.5,
            },
        };
        Css.SetStyle(vtOldGhost, "position: absolute; left: 14px; top: 18px; width: 120px; height: 46px; border-radius: 6px;");
        vtStage.Children.Add(vtOldGhost);

        var vtMidCard = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromArgb(190, 14, 116, 144)),
            Padding = new Thickness(8, 5, 8, 5),
            Child = new TextBlock
            {
                Text = "view-transition: hero-card\nSeek(0.50) -> [48, 76, 136x54]",
                Foreground = Brushes.White,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(vtMidCard, "position: absolute; left: 42px; top: 76px; width: 176px; height: 54px; border-radius: 7px;");
        vtStage.Children.Add(vtMidCard);

        var vtNewTarget = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromRgb(6, 95, 70)),
            Padding = new Thickness(8, 5, 8, 5),
            Child = new TextBlock
            {
                Text = "New Snapshot (t=1.0)\n[74, 144, 144x56]",
                Foreground = new SolidColorBrush(Color.FromRgb(209, 250, 229)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
            },
        };
        Css.SetStyle(vtNewTarget, "position: absolute; left: 82px; top: 144px; width: 158px; height: 56px; border-radius: 7px;");
        vtStage.Children.Add(vtNewTarget);

        panel3.Children.Add(vtStage);
        row.Children.Add(panel3);

        root.Children.Add(row);
        return root;
    }

    private static void Evaluate(DependencyObject owner)
    {
        CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner));
        CssEvaluationScheduler.FlushIfPending(owner.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher ?? Dispatcher.CurrentDispatcher);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }
    }
}
