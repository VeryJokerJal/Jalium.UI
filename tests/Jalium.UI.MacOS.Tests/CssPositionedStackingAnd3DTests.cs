using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Media3D;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssPositionedStackingAnd3DTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

    public void Dispose() => _context.Dispose();

    [Fact]
    public void CssAuthoringAndStyleQueriesForL4PropertiesWork()
    {
        foreach (var prop in new[]
        {
            "position", "inset", "top", "right", "bottom", "left", "z-index",
            "transform", "transform-origin", "transform-style",
            "perspective", "perspective-origin", "backface-visibility"
        })
        {
            Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty(prop).Support);
        }

        var sheet = CssStyleSheet.Parse("""
            @supports (perspective: 800px) and (transform-style: preserve-3d) and (backface-visibility: hidden) and (transform-origin: 50% 50% 40px) and (transform: rotateY(45deg) translateZ(20px)) {
                StackPanel.card3d {
                    position: relative;
                    z-index: 7;
                    perspective: 800px;
                    perspective-origin: 25% 75%;
                    transform-style: preserve-3d;
                    backface-visibility: hidden;
                    transform-origin: 20px 30px 40px;
                    transform: translate3d(10px, 20px, 30px) rotateZ(0deg);
                }
            }
            @container style(transform-style: preserve-3d) and style(backface-visibility: hidden) and style(perspective: 800px) and style(z-index: 7) {
                Border.query-probe {
                    width: 64px;
                }
            }
            """);

        var root = new StackPanel { Width = 200, Height = 200 };
        Css.SetStyleSheets(root, new CssStyleSheetCollection { sheet });

        var card = new StackPanel { Width = 100, Height = 80 };
        Css.SetClass(card, "card3d");
        var probe = new Border { Height = 20 };
        Css.SetClass(probe, "query-probe");
        card.Children.Add(probe);
        root.Children.Add(card);

        Layout(root, 200, 200);

        Assert.Equal(CssPositionMode.Relative, card.CssLayout?.Position);
        Assert.Equal(7, Panel.GetZIndex(card));
        Assert.Equal(800.0, CssTransform3D.GetPerspective(card));
        Assert.Equal(CssTransformStyle.Preserve3D, CssTransform3D.GetTransformStyle(card));
        Assert.Equal(CssBackfaceVisibility.Hidden, CssTransform3D.GetBackfaceVisibility(card));
        Assert.Equal(40.0, CssTransform3D.GetOriginZ(card));
        Assert.True(CssTransform3D.Needs3DRendering(card));
        Assert.Equal(64.0, probe.ActualWidth);
    }

    [Fact]
    public void ArbitraryHostContainersAutoInsetStaticPositionAndAutoMarginCenteringWork()
    {
        // 1. Canvas & UniformGrid host support + preceding sibling static position in StackPanel
        var stack = new StackPanel { Width = 200, Height = 160 };
        Css.SetStyle(stack, "position: relative;");
        var firstBlock = new Border { Width = 100, Height = 36, Background = Brushes.Gray };
        var autoAbsInStack = new Border { Width = 50, Height = 24, Background = Brushes.Red };
        Css.SetStyle(autoAbsInStack, "position: absolute; left: auto; top: auto; width: 50px; height: 24px;");
        stack.Children.Add(firstBlock);
        stack.Children.Add(autoAbsInStack);
        Layout(stack, 200, 160);
        Assert.Equal(2, stack.Children.Count);
        Assert.Equal(new Rect(0, 36, 50, 24), autoAbsInStack.VisualBounds);

        // 2. RTL auto static position
        var rtlHost = new StackPanel { Width = 200, Height = 100, FlowDirection = FlowDirection.RightToLeft };
        Css.SetStyle(rtlHost, "position: relative;");
        var rtlAbs = new Border { Width = 60, Height = 20 };
        Css.SetStyle(rtlAbs, "position: absolute; width: 60px; height: 20px;");
        rtlHost.Children.Add(rtlAbs);
        Layout(rtlHost, 200, 100);
        Assert.Equal(new Rect(140, 0, 60, 20), rtlAbs.VisualBounds);

        // 3. Overconstrained auto-margin centering (horizontal + vertical)
        var centerHost = new Grid { Width = 200, Height = 120 };
        Css.SetStyle(centerHost, "position: relative;");
        var centeredAbs = new Border();
        Css.SetStyle(centeredAbs, "position: absolute; left: 20px; right: 20px; top: 10px; bottom: 10px; width: 80px; height: 40px; margin: auto;");
        centerHost.Children.Add(centeredAbs);
        Layout(centerHost, 200, 120);
        // Available horizontal between 20 and 180 is 160 -> (160 - 80)/2 = 40 -> X = 20 + 40 = 60
        // Available vertical between 10 and 110 is 100 -> (100 - 40)/2 = 30 -> Y = 10 + 30 = 40
        Assert.Equal(new Rect(60, 40, 80, 40), centeredAbs.VisualBounds);

        // 4. Flex static alignment when insets are auto
        var flexHost = new FlexPanel { Width = 200, Height = 100 };
        Css.SetStyle(flexHost, "position: relative; display: flex; justify-content: center; align-items: flex-end;");
        var flexAbs = new Border();
        Css.SetStyle(flexAbs, "position: absolute; width: 60px; height: 30px;");
        flexHost.Children.Add(flexAbs);
        Layout(flexHost, 200, 100);
        Assert.Equal(new Rect(70, 70, 60, 30), flexAbs.VisualBounds);

        // 5. Canvas & UniformGrid & Border & ContentControl arbitrary hosts
        var canvas = new Canvas { Width = 160, Height = 100 };
        Css.SetStyle(canvas, "position: relative;");
        var canvasAbs = new Border();
        Css.SetStyle(canvasAbs, "position: absolute; right: 12px; bottom: 8px; width: 40px; height: 20px;");
        canvas.Children.Add(canvasAbs);
        Layout(canvas, 160, 100);
        Assert.Equal(new Rect(108, 72, 40, 20), canvasAbs.VisualBounds);

        var uniformGrid = new UniformGrid { Width = 160, Height = 100, Rows = 2, Columns = 2 };
        Css.SetStyle(uniformGrid, "position: relative;");
        var cell1 = new Border { Background = Brushes.Blue };
        var ugAbs = new Border { Background = Brushes.Red };
        Css.SetStyle(ugAbs, "position: absolute; left: 15px; top: 25px; width: 50px; height: 30px;");
        uniformGrid.Children.Add(cell1);
        uniformGrid.Children.Add(ugAbs);
        Layout(uniformGrid, 160, 100);
        Assert.Equal(new Rect(15, 25, 50, 30), ugAbs.VisualBounds);

        var borderHost = new Border { Width = 160, Height = 100 };
        Css.SetStyle(borderHost, "position: relative; border: 4px solid #333333;");
        var borderAbs = new Border();
        Css.SetStyle(borderAbs, "position: absolute; right: 10px; bottom: 10px; width: 40px; height: 20px;");
        borderHost.Child = borderAbs;
        Layout(borderHost, 160, 100);
        // Padding box inside 4px border is (4, 4, 152, 92) -> right 10 => 4 + 152 - 10 - 40 = 106; bottom 10 => 4 + 92 - 10 - 20 = 66
        Assert.Equal(new Rect(106, 66, 40, 20), borderAbs.VisualBounds);
        Assert.Same(borderAbs, borderHost.Child);

        // Round-trip: removing position: absolute restores normal flow
        Css.SetStyle(autoAbsInStack, "width: 50px; height: 24px;");
        Layout(stack, 200, 160);
        Assert.Equal(36, autoAbsInStack.VisualBounds.Y);
    }

    [Fact]
    public void ContainingBlockChainsAndComplexOverflowClipCombinationsWork()
    {
        // 1. Absolute skips static ancestors and intermediate overflow: hidden to reach positioned root
        var root = new Grid { Width = 240, Height = 180, Background = Brushes.White };
        Css.SetStyle(root, "position: relative;");

        var overflowMiddle = new StackPanel
        {
            Width = 80,
            Height = 60,
            Margin = new Thickness(100, 80, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.LightGray,
        };
        Css.SetStyle(overflowMiddle, "overflow: hidden;");

        var absEscaping = new Border { Background = Brushes.Red };
        Css.SetStyle(absEscaping, "position: absolute; left: 12px; top: 12px; width: 40px; height: 30px;");
        overflowMiddle.Children.Add(absEscaping);
        root.Children.Add(overflowMiddle);

        Layout(root, 240, 180);

        // Because root (not overflowMiddle) is the containing block, absEscaping is placed at (12, 12) in root
        // AND escapes overflowMiddle's overflow: hidden clip!
        var bitmap1 = Render(root);
        Assert.Equal(Colors.Red, Pixel(bitmap1, 20, 20));
        Assert.Same(absEscaping, root.HitTest(new Point(20, 20))?.VisualHit);
        Assert.Same(absEscaping, root.InputHitTest(new Point(20, 20)));

        // 2. When overflowMiddle itself becomes a containing block (e.g. via position: relative or transform or container-type),
        // absEscaping is now positioned relative to overflowMiddle AND clipped by overflowMiddle's overflow: hidden!
        Css.SetStyle(overflowMiddle, "position: relative; overflow: hidden;");
        Css.SetStyle(absEscaping, "position: absolute; left: -30px; top: 10px; width: 40px; height: 30px;");
        Layout(root, 240, 180);

        var bitmap2 = Render(root);
        // Point (80, 95) is outside overflowMiddle (100..180, 80..140) where left: -30px protrudes -> clipped!
        Assert.Equal(Colors.White, Pixel(bitmap2, 80, 95));
        Assert.NotSame(absEscaping, root.HitTest(new Point(80, 95))?.VisualHit);
        // Point (105, 95) is inside overflowMiddle -> visible Red!
        Assert.Equal(Colors.Red, Pixel(bitmap2, 105, 95));
        Assert.Same(absEscaping, root.HitTest(new Point(105, 95))?.VisualHit);

        // 3. Transform / Perspective / Filter / Container-Type establishes containing block for position: fixed!
        var fixedRoot = new Grid { Width = 240, Height = 180, Background = Brushes.White };
        var transformedAncestor = new StackPanel
        {
            Width = 100,
            Height = 80,
            Margin = new Thickness(60, 50, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Blue,
        };
        Css.SetStyle(transformedAncestor, "perspective: 600px;");
        var fixedChild = new Border { Background = Brushes.Red };
        Css.SetStyle(fixedChild, "position: fixed; left: 10px; top: 10px; width: 30px; height: 20px;");
        transformedAncestor.Children.Add(fixedChild);
        fixedRoot.Children.Add(transformedAncestor);

        Layout(fixedRoot, 240, 180);
        var bitmap3 = Render(fixedRoot);
        // Because transformedAncestor has perspective: 600px, fixedChild is positioned at (60+10, 50+10) = (70, 60), NOT (10, 10)!
        Assert.Equal(Colors.White, Pixel(bitmap3, 15, 15));
        Assert.Equal(Colors.Red, Pixel(bitmap3, 75, 65));
        Assert.Same(fixedChild, fixedRoot.HitTest(new Point(75, 65))?.VisualHit);
    }

    [Fact]
    public void CrossHierarchyStackingContextsAndNegativeZIndexWork()
    {
        // 1. z-index on position: static non-flex/non-grid is ignored for painting
        var staticTestRoot = new Grid { Width = 120, Height = 100, Background = Brushes.White };
        var staticHighZ = new Border { Width = 80, Height = 60, Background = Brushes.Red };
        Css.SetStyle(staticHighZ, "position: static; z-index: 99;");
        var staticLater = new Border { Width = 80, Height = 60, Background = Brushes.Blue };
        Css.SetStyle(staticLater, "position: static;");
        // Put inside a block container where children overlap via negative margin
        var blockContainer = new StackPanel { Width = 120, Height = 100 };
        Css.SetStyle(staticLater, "position: static; margin-top: -60px;");
        blockContainer.Children.Add(staticHighZ);
        blockContainer.Children.Add(staticLater);
        staticTestRoot.Children.Add(blockContainer);
        Layout(staticTestRoot, 120, 100);
        // Computed Panel.GetZIndex is still 99, but EffectiveZIndex is 0 since position is static in block flow, so staticLater paints on top!
        Assert.Equal(99, Panel.GetZIndex(staticHighZ));
        Assert.Equal(0, CssCompositingProperties.EffectiveZIndex(staticHighZ));
        Assert.Equal(Colors.Blue, Pixel(Render(staticTestRoot), 30, 30));

        // 2. Cross-hierarchy z-index competition across non-stacking-context ancestors
        var root = new Grid { Width = 200, Height = 140, Background = Brushes.White };
        var branchA = new StackPanel { Width = 200, Height = 140 };
        Css.SetStyle(branchA, "position: relative;"); // z-index: auto -> does NOT create stacking context
        var childA = new Border { Background = Brushes.Red };
        Css.SetStyle(childA, "position: absolute; left: 20px; top: 20px; width: 80px; height: 80px; z-index: 10;");
        branchA.Children.Add(childA);

        var branchB = new StackPanel { Width = 200, Height = 140 };
        Css.SetStyle(branchB, "position: relative;"); // z-index: auto -> does NOT create stacking context
        var childB = new Border { Background = Brushes.Blue };
        Css.SetStyle(childB, "position: absolute; left: 20px; top: 20px; width: 80px; height: 80px; z-index: 5;");
        branchB.Children.Add(childB);

        root.Children.Add(branchA);
        root.Children.Add(branchB);
        Layout(root, 200, 140);

        // Even though branchB is after branchA in DOM order, childA (z-index: 10) beats childB (z-index: 5) in root's stacking context!
        Assert.Equal(Colors.Red, Pixel(Render(root), 50, 50));
        Assert.Same(childA, root.HitTest(new Point(50, 50))?.VisualHit);

        // Now make branchA a stacking context with z-index: 1 and branchB a stacking context with z-index: 2
        Css.SetStyle(branchA, "position: relative; z-index: 1;");
        Css.SetStyle(branchB, "position: relative; z-index: 2;");
        Layout(root, 200, 140);

        // Now childA (z-index: 10) is trapped inside branchA (z-index: 1), so branchB (z-index: 2) and childB paint on top!
        Assert.Equal(Colors.Blue, Pixel(Render(root), 50, 50));
        Assert.Same(childB, root.HitTest(new Point(50, 50))?.VisualHit);

        // Round-trip: removing explicit z-index from branchA and branchB restores childA on top!
        Css.SetStyle(branchA, "position: relative;");
        Css.SetStyle(branchB, "position: relative;");
        Layout(root, 200, 140);
        Assert.Equal(Colors.Red, Pixel(Render(root), 50, 50));
        Assert.Same(childA, root.HitTest(new Point(50, 50))?.VisualHit);

        // 3. Negative z-index paints above stacking context background, below non-stacking parent background
        var negRoot = new Grid { Width = 160, Height = 120, Background = Brushes.White };
        Css.SetStyle(negRoot, "position: relative; z-index: 0;"); // establishes stacking context
        var nonStackingParent = new Border
        {
            Width = 100,
            Height = 80,
            Margin = new Thickness(40, 20, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Blue,
        };
        var negChild = new Border { Background = Brushes.Red };
        Css.SetStyle(negChild, "position: absolute; left: 10px; top: 20px; width: 60px; height: 60px; z-index: -1;");
        nonStackingParent.Child = negChild;
        negRoot.Children.Add(nonStackingParent);

        Layout(negRoot, 160, 120);
        var negBitmap = Render(negRoot);
        // At (25, 45), negChild is outside nonStackingParent (40..140) but inside negRoot -> Red (above White root background)!
        Assert.Equal(Colors.Red, Pixel(negBitmap, 25, 45));
        // At (55, 45), negChild overlaps nonStackingParent -> Blue (nonStackingParent paints on top of z-index: -1)!
        Assert.Equal(Colors.Blue, Pixel(negBitmap, 55, 45));
    }

    [Fact]
    public void Css3DTransformsPreserve3DBackfaceVisibilityAndInverseProjectedHitTestingWork()
    {
        // 1. Backface-visibility: hidden vs visible on 180deg Y rotation
        var bfRoot = new Grid { Width = 160, Height = 120, Background = Brushes.White };
        var flippedHidden = new Border
        {
            Width = 80,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20, 20, 0, 0),
            Background = Brushes.Red,
        };
        Css.SetStyle(flippedHidden, "transform: rotateY(180deg); backface-visibility: hidden;");
        bfRoot.Children.Add(flippedHidden);
        Layout(bfRoot, 160, 120);

        // Culled by backface-visibility: hidden -> White background and not hit!
        Assert.Equal(Colors.White, Pixel(Render(bfRoot), 50, 50));
        Assert.NotSame(flippedHidden, bfRoot.HitTest(new Point(50, 50))?.VisualHit);

        // Switching to backface-visibility: visible renders and hits the flipped plane!
        Css.SetStyle(flippedHidden, "transform: rotateY(180deg); backface-visibility: visible;");
        Layout(bfRoot, 160, 120);
        Assert.Equal(Colors.Red, Pixel(Render(bfRoot), 50, 50));
        Assert.Same(flippedHidden, bfRoot.HitTest(new Point(50, 50))?.VisualHit);

        // 2. transform-style: preserve-3d depth sorting vs flat
        var sceneRoot = new Grid { Width = 200, Height = 160, Background = Brushes.White };
        Css.SetStyle(sceneRoot, "perspective: 500px;");

        var stage = new Grid
        {
            Width = 120,
            Height = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(40, 30, 0, 0),
        };
        Css.SetStyle(stage, "position: relative; transform-style: preserve-3d;");

        // First in DOM: frontPlane at translateZ(60px) (Red)
        var frontPlane = new Border { Width = 80, Height = 80, Background = Brushes.Red };
        Css.SetStyle(frontPlane, "position: absolute; left: 20px; top: 10px; width: 80px; height: 80px; transform: translateZ(60px);");

        // Second in DOM: backPlane at translateZ(-60px) (Blue)
        var backPlane = new Border { Width = 80, Height = 80, Background = Brushes.Blue };
        Css.SetStyle(backPlane, "position: absolute; left: 20px; top: 10px; width: 80px; height: 80px; transform: translateZ(-60px);");

        stage.Children.Add(frontPlane);
        stage.Children.Add(backPlane);
        sceneRoot.Children.Add(stage);

        Layout(sceneRoot, 200, 160);
        // Under preserve-3d, frontPlane (Z = +60) paints in front of backPlane (Z = -60) even though backPlane is later in DOM!
        Assert.Equal(Colors.Red, Pixel(Render(sceneRoot), 100, 80));
        Assert.Same(frontPlane, sceneRoot.HitTest(new Point(100, 80))?.VisualHit);

        // Switching stage to transform-style: flat flattens children before depth sorting, so DOM order (backPlane) wins!
        Css.SetStyle(stage, "position: relative; transform-style: flat;");
        Layout(sceneRoot, 200, 160);
        Assert.Equal(Colors.Blue, Pixel(Render(sceneRoot), 100, 80));
        Assert.Same(backPlane, sceneRoot.HitTest(new Point(100, 80))?.VisualHit);

        // 3. Projective foreshortening and inverse-projected hit testing
        var projRoot = new Grid { Width = 240, Height = 200, Background = Brushes.White };
        var projCard = new Border
        {
            Width = 120,
            Height = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(60, 50, 0, 0),
            Background = Brushes.Red,
        };
        // Perspective(200px) rotateY(45deg) around center (60, 50):
        // Left edge (x=0 -> dx=-60): z = -(-60)*sin(45) = +42.42 (closer to camera, w' = 1 - 42.42/200 = 0.788 -> expands vertically!)
        // Right edge (x=120 -> dx=+60): z = -60*sin(45) = -42.42 (farther from camera, w' = 1 + 42.42/200 = 1.212 -> shrinks vertically!)
        Css.SetStyle(projCard, "transform-origin: 50% 50% 0px; transform: perspective(200px) rotateY(45deg);");
        projRoot.Children.Add(projCard);
        Layout(projRoot, 240, 200);

        var projBounds = projCard.GetRenderBounds();
        // Because left edge expands vertically beyond 100px, projBounds.Height > 110!
        Assert.True(projBounds.Height > 110, $"Expected perspective-expanded height > 110, got {projBounds.Height}");

        var toRoot = projCard.TransformToVisual(projRoot);
        var projectedTopLeft = toRoot.Transform(new Point(0, 0));
        var projectedTopRight = toRoot.Transform(new Point(120, 0));
        // Top-left Y is higher (smaller Y) than original 50, while top-right Y is lower (larger Y) than original 50!
        Assert.True(projectedTopLeft.Y < 45, $"Expected projectedTopLeft.Y < 45, got {projectedTopLeft.Y}");
        Assert.True(projectedTopRight.Y > 55, $"Expected projectedTopRight.Y > 55, got {projectedTopRight.Y}");

        // Center of card (120, 100) hits projCard
        Assert.Same(projCard, projRoot.HitTest(new Point(120, 100))?.VisualHit);

        // Point near top-right corner of original unrotated box (160, 52) is OUTSIDE the foreshortened right edge!
        Assert.NotSame(projCard, projRoot.HitTest(new Point(160, 52))?.VisualHit);

        // 4. 3D Transform Interpolation (Quaternion slerp + 3D translation)
        var r1 = new CssTokenReader("translate3d(0px, 0px, 0px) rotateY(0deg)");
        var r2 = new CssTokenReader("translate3d(100px, 40px, 80px) rotateY(90deg)");
        Assert.True(CssTransformParser.TryParseTransformList(ref r1, out var fromVal));
        Assert.True(CssTransformParser.TryParseTransformList(ref r2, out var toVal));
        var midVal = CssTransformInterpolation.Interpolate(fromVal, toVal, 0.5);
        Assert.NotNull(midVal);
        var midM = CssTransform3D.GetTransformMatrix3D(midVal);
        Assert.Equal(50, midM.OffsetX, 1);
        Assert.Equal(20, midM.OffsetY, 1);
        Assert.Equal(40, midM.OffsetZ, 1);
        // At 45deg around Y: M11 = cos(45deg) ≈ 0.7071
        Assert.Equal(Math.Cos(Math.PI / 4.0), midM.M11, 2);
    }

    [Fact]
    public void RenderL4PositionedStackingAnd3DVisualsInDualWindowAndDpi()
    {
        var outDir = Environment.GetEnvironmentVariable("JALIUM_L4_SCREENSHOT_DIR")
            ?? "/Volumes/My PSSD/.codex-css-acceptance/css-l4-positioned-stacking-3d-20261026/screenshots";
        Directory.CreateDirectory(outDir);

        foreach (var (windowStyle, modeName) in new[]
        {
            (WindowStyle.SingleBorderWindow, "native"),
            (WindowStyle.None, "withoutborder")
        })
        {
            foreach (var (dpi, suffix) in new[] { (1.0, "1x"), (2.0, "2x") })
            {
                var visual = BuildL4ShowcaseVisual(windowStyle);
                VisualTreeHelper.SetRootDpi(visual, new DpiScale(dpi, dpi));
                var width = 820;
                var height = 540;
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

                var path = Path.Combine(outDir, $"l4-positioned-stacking-3d-{modeName}-{suffix}.png");
                var bmp = BitmapSource.Create(pixelWidth, pixelHeight, 96.0 * dpi, 96.0 * dpi, PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(path);
                encoder.Save(stream);
                Assert.True(new FileInfo(path).Length > 1024);
            }
        }
    }

    private static FrameworkElement BuildL4ShowcaseVisual(WindowStyle windowStyle)
    {
        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 15, 25)),
        };
        Css.SetStyle(root, "display: block; width: 820px; height: 540px; padding: 18px; position: relative;");

        // Header bar showing window style mode & L4 milestone capabilities
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 30, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new TextBlock
            {
                Text = $"L4 Positioning, Stacking Contexts & CSS 3D — Window Mode: {(windowStyle == WindowStyle.None ? "WithoutBorder" : "Native Chrome")}",
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(header, "display: block; margin-bottom: 14px; border-radius: 8px;");
        root.Children.Add(header);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        // Panel 1: Containing Block Chains, Auto-Margin Centering & Overflow Clip Escape
        var panel1 = new Grid
        {
            Width = 250,
            Height = 440,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
        };
        Css.SetStyle(panel1, "position: relative; border-radius: 10px; border: 1px solid #334155; padding: 12px;");

        var p1Title = new TextBlock
        {
            Text = "1. Containing Blocks & Auto Insets",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        };
        Css.SetStyle(p1Title, "position: absolute; left: 14px; top: 12px;");
        panel1.Children.Add(p1Title);

        // Auto-margin centered card inside containing block
        var centeredCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new TextBlock
            {
                Text = "Auto-Margin Centered\n(inset: 44px 16px auto 16px)",
                Foreground = Brushes.White,
                FontSize = 11,
            },
        };
        Css.SetStyle(centeredCard, "position: absolute; left: 16px; right: 16px; top: 44px; width: 180px; height: 54px; margin-left: auto; margin-right: auto; border-radius: 8px;");
        panel1.Children.Add(centeredCard);

        // Intermediate overflow: hidden box whose absolute child escapes to panel1
        var clippedBox = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Margin = new Thickness(16, 120, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 170,
            Height = 90,
        };
        Css.SetStyle(clippedBox, "overflow: hidden; border-radius: 8px; border: 1px solid #475569; padding: 8px;");
        clippedBox.Children.Add(new TextBlock
        {
            Text = "Unpositioned overflow:hidden\n(Absolute child escapes clip)",
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 11,
        });

        var escapingBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
            Padding = new Thickness(10, 6, 10, 6),
            Child = new TextBlock
            {
                Text = "Escaped Absolute Badge (top: 195px)",
                Foreground = new SolidColorBrush(Color.FromRgb(6, 78, 59)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(escapingBadge, "position: absolute; left: 26px; top: 195px; width: 204px; height: 32px; border-radius: 6px;");
        clippedBox.Children.Add(escapingBadge);
        panel1.Children.Add(clippedBox);

        // Perspective-established fixed containing block
        var fixedHost = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 27, 75)),
            Margin = new Thickness(16, 260, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 216,
            Height = 140,
        };
        Css.SetStyle(fixedHost, "perspective: 500px; border-radius: 8px; border: 1px solid #6366f1; padding: 10px;");
        fixedHost.Children.Add(new TextBlock
        {
            Text = "Fixed Containing Block\n(perspective: 500px captures fixed)",
            Foreground = new SolidColorBrush(Color.FromRgb(199, 210, 254)),
            FontSize = 11,
        });
        var capturedFixed = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            Padding = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text = "fixed; right: 12px; bottom: 12px",
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(capturedFixed, "position: fixed; right: 12px; bottom: 12px; width: 192px; height: 34px; border-radius: 6px;");
        fixedHost.Children.Add(capturedFixed);
        panel1.Children.Add(fixedHost);

        row.Children.Add(panel1);

        // Panel 2: Cross-Hierarchy Stacking Contexts & Negative Z-Index
        var panel2 = new Grid
        {
            Width = 250,
            Height = 440,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
        };
        Css.SetStyle(panel2, "position: relative; z-index: 0; border-radius: 10px; border: 1px solid #334155; padding: 12px;");

        var p2Title = new TextBlock
        {
            Text = "2. Cross-Hierarchy Stacking Contexts",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        };
        Css.SetStyle(p2Title, "position: absolute; left: 14px; top: 12px;");
        panel2.Children.Add(p2Title);

        var branch1 = new StackPanel();
        Css.SetStyle(branch1, "position: relative;"); // z-index: auto
        var highZCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(225, 29, 72)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(251, 113, 133)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "Branch A Child (z-index: 20)\nWins over Branch B (z: 5)",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(highZCard, "position: absolute; left: 20px; top: 56px; width: 178px; height: 76px; z-index: 20; border-radius: 8px;");
        branch1.Children.Add(highZCard);

        var branch2 = new StackPanel();
        Css.SetStyle(branch2, "position: relative;"); // z-index: auto
        var lowZCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "Branch B Child (z-index: 5)\nLater in DOM, painted below",
                Foreground = Brushes.White,
                FontSize = 11,
            },
        };
        Css.SetStyle(lowZCard, "position: absolute; left: 52px; top: 96px; width: 178px; height: 76px; z-index: 5; border-radius: 8px;");
        branch2.Children.Add(lowZCard);

        panel2.Children.Add(branch1);
        panel2.Children.Add(branch2);

        // Negative z-index demonstration
        var negUnderlay = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(124, 58, 237)),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new TextBlock
            {
                Text = "Negative Z-Index (z-index: -1)\nAbove root, below in-flow",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            },
        };
        Css.SetStyle(negUnderlay, "position: absolute; left: 18px; top: 230px; width: 210px; height: 82px; z-index: -1; border-radius: 8px;");

        var inFlowForeground = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(34, 286, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 196,
            Height = 90,
            Padding = new Thickness(10),
        };
        Css.SetStyle(inFlowForeground, "border-radius: 8px;");
        inFlowForeground.Child = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "In-Flow Block Surface",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                },
                new TextBlock
                {
                    Text = "Paints above z-index: -1\nunderlay inside same root",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 11,
                },
                negUnderlay
            }
        };
        panel2.Children.Add(inFlowForeground);

        row.Children.Add(panel2);

        // Panel 3: CSS 3D Transforms, Perspective & Preserve-3D Depth Sorting
        var panel3 = new Grid
        {
            Width = 250,
            Height = 440,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
        };
        Css.SetStyle(panel3, "position: relative; perspective: 420px; perspective-origin: 50% 45%; border-radius: 10px; border: 1px solid #334155; padding: 12px;");

        var p3Title = new TextBlock
        {
            Text = "3. CSS 3D & Preserve-3D Planes",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        };
        Css.SetStyle(p3Title, "position: absolute; left: 14px; top: 12px;");
        panel3.Children.Add(p3Title);

        // Preserve-3D tilted stage with intersecting/depth-sorted planes
        var stage3D = new Grid
        {
            Width = 200,
            Height = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24, 48, 0, 0),
        };
        Css.SetStyle(stage3D, "transform-style: preserve-3d; transform: rotateX(14deg) rotateY(-24deg);");

        var planeFront = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(94, 234, 212)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "Preserve-3D Front Plane\ntranslateZ(42px)",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
            },
        };
        Css.SetStyle(planeFront, "position: absolute; left: 14px; top: 28px; width: 156px; height: 92px; border-radius: 8px; transform: translateZ(42px);");

        var planeBack = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "Preserve-3D Back Plane\ntranslateZ(-36px)",
                Foreground = Brushes.White,
                FontSize = 11,
            },
        };
        Css.SetStyle(planeBack, "position: absolute; left: 36px; top: 10px; width: 156px; height: 92px; border-radius: 8px; transform: translateZ(-36px);");

        // Add front first, back second: preserve-3d depth sort keeps planeFront in front!
        stage3D.Children.Add(planeFront);
        stage3D.Children.Add(planeBack);
        panel3.Children.Add(stage3D);

        // Projective tilted card with 3-value transform-origin
        var tiltedCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(28, 255, 0, 0),
            Width = 190,
            Height = 120,
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = "Projective 3D Card\nperspective(320px) rotateY(28deg) rotateX(12deg)\n3-Value Origin: 50% 50% -20px",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Css.SetStyle(tiltedCard, "border-radius: 10px; transform-origin: 50% 50% -20px; transform: perspective(320px) rotateY(28deg) rotateX(12deg);");
        panel3.Children.Add(tiltedCard);

        row.Children.Add(panel3);
        root.Children.Add(row);
        return root;
    }

    private static void Layout(UIElement root, double width, double height)
    {
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }

    private static RenderTargetBitmap Render(FrameworkElement root)
    {
        var target = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormat.Bgra32);
        target.Render(root);
        return target;
    }

    private static Color Pixel(RenderTargetBitmap target, int x, int y)
    {
        var pixel = new byte[4];
        target.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
