using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSizingDeterminismBoundariesTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

    public void Dispose() => _context.Dispose();

    [Fact]
    public void BottomMarginCollapsingHonorsMinHeightAndIntrinsicPreferredHeightBoundaries()
    {
        // Case 1: min-height is satisfied by content (30 >= 20), so child's 18px bottom margin escapes
        // and collapses with the following sibling's 10px top margin -> sibling Y = 30 + max(18, 10) = 48.
        var root1 = new StackPanel();
        var parent1 = new StackPanel();
        var child1 = new Border();
        var sibling1 = new Border();
        parent1.Children.Add(child1);
        root1.Children.Add(parent1);
        root1.Children.Add(sibling1);
        Css.SetStyle(root1, "display:flow-root;width:220px;");
        Css.SetStyle(parent1, "display:block;min-height:20px;");
        Css.SetStyle(child1, "display:block;height:30px;margin-bottom:18px;");
        Css.SetStyle(sibling1, "display:block;height:24px;margin-top:10px;");
        Layout(root1, 220, 200);
        Near(30, parent1.ActualHeight);
        Near(48, sibling1.TransformToAncestor(root1).Y);

        // Case 2: min-height (50px) clamps tentative content height (30px), so child's 25px bottom margin
        // does NOT escape and does NOT add onto cursor to exceed min-height (parent height is 50, not 55).
        var root2 = new StackPanel();
        var parent2 = new StackPanel();
        var child2 = new Border();
        var sibling2 = new Border();
        parent2.Children.Add(child2);
        root2.Children.Add(parent2);
        root2.Children.Add(sibling2);
        Css.SetStyle(root2, "display:flow-root;width:220px;");
        Css.SetStyle(parent2, "display:block;min-height:50px;");
        Css.SetStyle(child2, "display:block;height:30px;margin-bottom:25px;");
        Css.SetStyle(sibling2, "display:block;height:24px;margin-top:10px;");
        Layout(root2, 220, 200);
        Near(50, parent2.ActualHeight);
        Near(60, sibling2.TransformToAncestor(root2).Y);

        // Case 3: intrinsic preferred height (height:max-content) prevents bottom margin escape.
        var root3 = new StackPanel();
        var parent3 = new StackPanel();
        var child3 = new Border();
        var sibling3 = new Border();
        parent3.Children.Add(child3);
        root3.Children.Add(parent3);
        root3.Children.Add(sibling3);
        Css.SetStyle(root3, "display:flow-root;width:220px;");
        Css.SetStyle(parent3, "display:block;height:max-content;");
        Css.SetStyle(child3, "display:block;height:30px;margin-bottom:22px;");
        Css.SetStyle(sibling3, "display:block;height:20px;");
        Layout(root3, 220, 200);
        Near(52, sibling3.TransformToAncestor(root3).Y);
    }

    [Fact]
    public void SelfCollapsingSiblingBeforeClearedSiblingStillEscapesParentTopMarginInFloatContext()
    {
        var root = new StackPanel();
        var floatBox = new Border();
        var parent = new StackPanel();
        var emptyCollapsible = new Border();
        var clearedChild = new Border();
        parent.Children.Add(emptyCollapsible);
        parent.Children.Add(clearedChild);
        root.Children.Add(floatBox);
        root.Children.Add(parent);

        Css.SetStyle(root, "display:flow-root;width:240px;");
        Css.SetStyle(floatBox, "display:block;float:left;width:60px;height:40px;");
        Css.SetStyle(parent, "display:block;");
        Css.SetStyle(emptyCollapsible, "display:block;margin-top:28px;height:0px;");
        Css.SetStyle(clearedChild, "display:block;clear:left;height:20px;");

        Layout(root, 240, 200);
        Near(28, parent.TransformToAncestor(root).Y);
        Near(40, clearedChild.TransformToAncestor(root).Y);
        Near(32, parent.ActualHeight);
    }

    [Fact]
    public void FitContentFunctionAndCalcSizePercentageTermsRespectCyclicAndDefiniteBases()
    {
        // 1. fit-content(calc(50% + 60px)) inside a cyclic max-content parent vs definite 200px parent.
        var root = new StackPanel();
        var cyclicParent = new StackPanel();
        var cyclicItem = new StackPanel();
        var wordA = new Border { Width = 40, Height = 16 };
        var wordB = new Border { Width = 140, Height = 16 };
        Css.SetStyle(wordA, "display:inline-block;");
        Css.SetStyle(wordB, "display:inline-block;");
        cyclicItem.Children.Add(wordA);
        cyclicItem.Children.Add(wordB);
        cyclicParent.Children.Add(cyclicItem);
        root.Children.Add(cyclicParent);

        Css.SetStyle(root, "display:flow-root;width:400px;");
        Css.SetStyle(cyclicParent, "display:block;width:max-content;");
        Css.SetStyle(cyclicItem, "display:block;width:fit-content(calc(50% + 60px));");

        Layout(root, 400, 200);
        // During cyclic probe of cyclicParent (width:max-content), preferred width fit-content(calc(50% + 60px))
        // treats the cyclic percentage as auto -> max-content (40 + 140 = 180px).
        // Then in layout against 180px, fit-content(calc(50% + 60px)) = fit-content(90 + 60 = 150px) clamped by [140, 180] = 150px.
        Near(180, cyclicParent.ActualWidth);
        Near(150, cyclicItem.ActualWidth);

        // Now make wordA=40, wordB=50 so min-content=50, max-content=90:
        // Probe: max-content = 90px -> parent width = 90px.
        // Layout against 90px: fit-content(45px + 60px = 105px) in [50, 90] = 90px.
        wordB.Width = 50;
        Layout(root, 400, 200);
        Near(90, cyclicParent.ActualWidth);
        Near(90, cyclicItem.ActualWidth);

        // Definite 200px parent with wordB=140 (max-content=180): fit-content(160px) in [140, 180] = 160px.
        wordB.Width = 140;
        Css.SetStyle(cyclicParent, "display:block;width:200px;");
        Layout(root, 400, 200);
        Near(200, cyclicParent.ActualWidth);
        Near(160, cyclicItem.ActualWidth);

        // 2. min-width:fit-content(calc(50% + 150px)) under both a cyclic max-content parent and an indefinite inline-block parent:
        // Cyclic/indefinite minimum percentage basis is 0, so fit-content(150px) in [130, 180] = 150px (not 180px).
        wordA.Width = 50;
        wordB.Width = 130;
        Css.SetStyle(cyclicParent, "display:block;width:max-content;");
        Css.SetStyle(cyclicItem, "display:block;width:20px;min-width:fit-content(calc(50% + 150px));");
        Layout(root, 400, 200);
        Near(150, cyclicParent.ActualWidth);
        Near(180, cyclicItem.ActualWidth);

        Css.SetStyle(cyclicParent, "display:inline-block;");
        Layout(root, 400, 200);
        Near(150, cyclicParent.ActualWidth);

        // 3. calc-size(min-content, size + 50%) inside a cyclic max-content parent:
        // During the cyclic probe, the calculation-body percentage resolves against 0 (130px, not 130 + 50% of 400 = 330px),
        // then resolves 130 + 50% of 130 = 195px in layout.
        Css.SetStyle(cyclicParent, "display:block;width:max-content;");
        Css.SetStyle(cyclicItem, "display:block;width:calc-size(min-content, size + 50%);");
        Layout(root, 400, 200);
        Near(130, cyclicParent.ActualWidth);
        Near(195, cyclicItem.ActualWidth);

        Css.SetStyle(cyclicItem, "display:block;width:20px;min-width:calc-size(min-content, size + 50%);");
        Layout(root, 400, 200);
        Near(130, cyclicParent.ActualWidth);
        Near(195, cyclicItem.ActualWidth);
    }

    [Fact]
    public void IndefiniteAndMultiLineRowFlexContainersResolveStretchedItemPercentagesAfterLineSizing()
    {
        // Case 1: Single-line row flex container with height:auto and max-height:200px.
        // The 200px max-height must NOT make height definite during item measurement;
        // once the line cross size settles at 60px (from sibling), the stretched item's 50% child resolves to 30px.
        var root1 = new StackPanel();
        var flex1 = new FlexPanel();
        var fixedItem = new Border { Width = 40, Height = 60 };
        var stretchItem = new Border { Width = 60 };
        var percentChild1 = new Border { Child = new Border { Width = 20, Height = 20 } };
        stretchItem.Child = percentChild1;
        flex1.Children.Add(fixedItem);
        flex1.Children.Add(stretchItem);
        root1.Children.Add(flex1);

        Css.SetStyle(root1, "display:flow-root;width:300px;");
        Css.SetStyle(flex1, "display:flex;flex-direction:row;align-items:stretch;width:200px;height:auto;max-height:200px;");
        Css.SetStyle(fixedItem, "display:block;");
        Css.SetStyle(stretchItem, "display:block;height:auto;");
        Css.SetStyle(percentChild1, "display:block;height:50%;");

        Layout(root1, 300, 240);
        Near(60, flex1.ActualHeight);
        Near(60, stretchItem.ActualHeight);
        Near(30, percentChild1.ActualHeight);

        // Case 2: Multi-line row flex container (flex-wrap:wrap; height:160px; align-content:stretch) with 2 lines:
        // Line 0 base cross size is 20px (50% degrades to auto during hypothetical cross sizing), Line 1 base cross size is 40px.
        // Free cross space is 160 - 60 = 100px (+50px per line), so Line 0 stretches to 70px and its 50% child resolves to 35px (not 80px).
        var root2 = new StackPanel();
        var flex2 = new FlexPanel();
        var line0Item = new Border { Width = 60 };
        var percentChild2 = new Border { Child = new Border { Width = 20, Height = 20 } };
        line0Item.Child = percentChild2;
        var line1Item = new Border { Width = 60, Height = 40 };
        flex2.Children.Add(line0Item);
        flex2.Children.Add(line1Item);
        root2.Children.Add(flex2);

        Css.SetStyle(root2, "display:flow-root;width:300px;");
        Css.SetStyle(flex2, "display:flex;flex-direction:row;flex-wrap:wrap;align-items:stretch;align-content:stretch;width:100px;height:160px;");
        Css.SetStyle(line0Item, "display:block;width:60px;height:auto;");
        Css.SetStyle(percentChild2, "display:block;height:50%;");
        Css.SetStyle(line1Item, "display:block;width:60px;height:40px;");

        Layout(root2, 300, 240);
        Near(160, flex2.ActualHeight);
        Near(70, line0Item.ActualHeight);
        Near(35, percentChild2.ActualHeight);

        // Case 3: Column FlexPanel stretched inside a definite-height row FlexPanel acquires a definite main height.
        var root3 = new StackPanel();
        var outerRow = new FlexPanel();
        var innerColumn = new FlexPanel();
        var columnPercentChild = new Border { Width = 40, Child = new Border { Width = 20, Height = 20 } };
        innerColumn.Children.Add(columnPercentChild);
        outerRow.Children.Add(innerColumn);
        root3.Children.Add(outerRow);

        Css.SetStyle(root3, "display:flow-root;width:300px;");
        Css.SetStyle(outerRow, "display:flex;flex-direction:row;align-items:stretch;width:200px;height:140px;");
        Css.SetStyle(innerColumn, "display:flex;flex-direction:column;align-items:flex-start;width:80px;height:auto;");
        Css.SetStyle(columnPercentChild, "display:block;height:50%;flex:none;");

        Layout(root3, 300, 240);
        Near(140, innerColumn.ActualHeight);
        Near(70, columnPercentChild.ActualHeight);
    }

    [Fact]
    public void GridSpanningIntrinsicMinimumCapsAtFiniteGrowthLimitsBeforeOverflowing()
    {
        var root = new StackPanel();
        var grid = new Grid();
        var spanItem = new StackPanel();
        var solidBlock = new Border { Width = 160, Height = 24 };
        Css.SetStyle(solidBlock, "display:block;");
        spanItem.Children.Add(solidBlock);

        var col0Probe = new Border { Height = 20 };
        var col1Probe = new Border { Height = 20 };
        grid.Children.Add(spanItem);
        grid.Children.Add(col0Probe);
        grid.Children.Add(col1Probe);
        root.Children.Add(grid);

        Css.SetStyle(root, "display:flow-root;width:300px;");
        Css.SetStyle(grid, "display:grid;width:240px;justify-content:start;grid-template-columns:minmax(min-content, 60px) auto;");
        Css.SetStyle(spanItem, "display:block;grid-column:1 / span 2;");
        Css.SetStyle(col0Probe, "display:block;grid-column:1;");
        Css.SetStyle(col1Probe, "display:block;grid-column:2;");

        Layout(root, 300, 200);
        Near(60, col0Probe.ActualWidth);
        Near(60, col1Probe.TransformToAncestor(grid).X);
        Near(100, col1Probe.ActualWidth);
    }

    [Fact]
    public void NestedRowSubgridPreservesRowContributionsAcrossMultiPassLayout()
    {
        var root = new StackPanel();
        var outerGrid = new Grid();
        var subgrid = new Grid();
        var subgridSpanItem = new Border { Width = 60, Height = 110 };
        var subgridRow1Item = new Border { Width = 60, Height = 20 };
        subgrid.Children.Add(subgridSpanItem);
        subgrid.Children.Add(subgridRow1Item);
        outerGrid.Children.Add(subgrid);
        root.Children.Add(outerGrid);

        Css.SetStyle(root, "display:flow-root;width:260px;");
        Css.SetStyle(outerGrid, "display:grid;width:240px;grid-template-columns:120px 120px;grid-template-rows:minmax(min-content, 40px) auto;align-items:start;");
        Css.SetStyle(subgrid, "display:grid;grid-column:1 / span 2;grid-row:1 / span 2;grid-template-columns:subgrid;grid-template-rows:subgrid;align-items:start;");
        Css.SetStyle(subgridSpanItem, "display:block;grid-column:1;grid-row:1 / span 2;");
        Css.SetStyle(subgridRow1Item, "display:block;grid-column:2;grid-row:2;");

        Layout(root, 260, 200);
        Near(110, outerGrid.ActualHeight);
        Near(40, subgridRow1Item.TransformToAncestor(outerGrid).Y);
        Near(20, subgridRow1Item.ActualHeight);
    }

    [Fact]
    public void AbsoluteElementWithBothInsetsEstablishesDefiniteHeightAndHonorsPaddingBoxAndBounds()
    {
        var root = new StackPanel();
        var host = new StackPanel();
        var absStretch = new Border();
        var percentChild = new Border { Child = new Border { Width = 20, Height = 20 } };
        absStretch.Child = percentChild;
        var absClamped = new Border();

        host.Children.Add(absStretch);
        host.Children.Add(absClamped);
        root.Children.Add(host);

        // Host outer box is 220x180 border-box with 10px border -> padding-box is 200x160 at (10, 10).
        Css.SetStyle(root, "display:flow-root;width:260px;");
        Css.SetStyle(host, "display:block;position:relative;box-sizing:border-box;width:220px;height:180px;border:10px solid #334155;padding:15px;");
        Css.SetStyle(absStretch, "display:block;position:absolute;left:15px;right:25px;top:10px;bottom:30px;box-sizing:border-box;");
        Css.SetStyle(percentChild, "display:block;height:50%;");
        Css.SetStyle(absClamped, "display:block;position:absolute;right:10px;bottom:15px;width:140px;max-width:80px;height:100px;max-height:50px;box-sizing:border-box;");

        Layout(root, 260, 220);

        // absStretch in 200x160 padding-box at (10,10): X = 10 + 15 = 25, Y = 10 + 10 = 20, Width = 200 - 40 = 160, Height = 160 - 40 = 120.
        Near(25, absStretch.VisualBounds.X);
        Near(20, absStretch.VisualBounds.Y);
        Near(160, absStretch.ActualWidth);
        Near(120, absStretch.ActualHeight);
        Near(60, percentChild.ActualHeight);

        // absClamped in 200x160 padding-box at (10,10): Width = 80, Height = 50, X = 10 + (200 - 10 - 80) = 120, Y = 10 + (160 - 15 - 50) = 105.
        Near(120, absClamped.VisualBounds.X);
        Near(105, absClamped.VisualBounds.Y);
        Near(80, absClamped.ActualWidth);
        Near(50, absClamped.ActualHeight);
    }

    [Theory]
    [InlineData("border")]
    [InlineData("content")]
    [InlineData("presenter")]
    public void NativeSingleChildHostsMatchPanelBoundariesForOutOfFlowAndIndefinitePercentages(string kind)
    {
        var root = new StackPanel();
        var leaf = new Border { Width = 40, Height = 36 };
        var percentBody = new Border { Child = leaf };
        Css.SetClass(percentBody, "body");

        FrameworkElement host = kind switch
        {
            "border" => new Border { Child = percentBody },
            "content" => new ContentControl { Content = percentBody },
            _ => new ContentPresenter { Content = percentBody },
        };
        Css.SetClass(host, "host");
        root.Children.Add(host);

        var sheet = CssStyleSheet.Parse("""
            .host {
                position: relative;
                box-sizing: border-box;
                width: 200px;
                border: 6px solid #475569;
                padding: 10px;
            }
            .host > .body {
                display: block;
                height: 50%;
            }
            .host::after {
                content: '';
                position: absolute;
                right: 8px;
                bottom: 6px;
                width: 24px;
                height: 18px;
            }
            """);
        Css.GetStyleSheets(root).Add(sheet);
        Css.SetStyle(root, "display:flow-root;width:260px;");

        Layout(root, 260, 200);

        // In-flow child's height:50% degrades to auto (36px) during indefinite-height MeasureOverride.
        // Host height = 36 + border(12) + padding(20) = 68px; padding-box is 188x56 at offset (6, 6).
        Near(200, host.ActualWidth);
        Near(68, host.ActualHeight);
        Near(16, percentBody.VisualBounds.X);
        Near(16, percentBody.VisualBounds.Y);

        var after = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(host))
            .Select(i => VisualTreeHelper.GetChild(host, i))
            .OfType<FrameworkElement>()
            .Single(e => !ReferenceEquals(e, percentBody));

        // ::after is placed in the 188x56 padding-box at (6, 6):
        // X = 6 + (188 - 8 - 24) = 162, Y = 6 + (56 - 6 - 18) = 38.
        Near(162, after.VisualBounds.X);
        Near(38, after.VisualBounds.Y);
        Near(24, after.ActualWidth);
        Near(18, after.ActualHeight);
    }

    [Fact]
    public async Task ConstraintRoundTripsProduceOrderIndependentGeometryAndRenderCleanly()
    {
        var (roundTripRoot, roundTripProbes) = BuildCompositeScene();
        foreach (var width in new[] { 0d, double.PositiveInfinity, 120d, 260d, 120d, 320d })
        {
            Layout(roundTripRoot, width, 360);
        }

        var (freshRoot, freshProbes) = BuildCompositeScene();
        Layout(freshRoot, 320, 360);

        Assert.Equal(freshProbes.Count, roundTripProbes.Count);
        foreach (var (key, freshElement) in freshProbes)
        {
            var actualElement = roundTripProbes[key];
            Near(freshElement.ActualWidth, actualElement.ActualWidth);
            Near(freshElement.ActualHeight, actualElement.ActualHeight);
            Near(freshElement.VisualBounds.X, actualElement.VisualBounds.X);
            Near(freshElement.VisualBounds.Y, actualElement.VisualBounds.Y);
        }

        Near(44, roundTripProbes["band1"].ActualHeight);
        Near(64, roundTripProbes["band1Sibling"].VisualBounds.Y);
        Near(42, roundTripProbes["flexItem0"].ActualHeight);
        Near(21, roundTripProbes["flexPercent"].ActualHeight);
        Near(60, roundTripProbes["nativeHost"].ActualHeight);

        // Verify worker-thread CommandList replay and render to PNG artifacts at 1x and 2x DPI.
        var outputDir = "/Volumes/My PSSD/.codex-css-acceptance/css-l1-sizing-determinism-20261026/screenshots";
        Directory.CreateDirectory(outputDir);

        foreach (var (scale, fileName) in new[] { (1.0, "l1-sizing-determinism-1x.png"), (2.0, "l1-sizing-determinism-2x.png") })
        {
            VisualTreeHelper.SetRootDpi(roundTripRoot, new DpiScale(scale, scale));
            Layout(roundTripRoot, 320, 360);

            int pixelWidth = (int)Math.Ceiling(320 * scale);
            int pixelHeight = (int)Math.Ceiling(360 * scale);
            var recorder = new DrawingRecorder();
            recorder.BindWholeFrame();
            roundTripRoot.Render(recorder);
            var frame = recorder.Commit();
            Assert.True(frame.IsFullyRecordable);

            var replayedPixels = await Task.Run(() =>
            {
                using var target = _context.CreateOffscreenRenderTarget(pixelWidth, pixelHeight);
                target.RequestReadback();
                target.BeginDraw();
                target.Clear(0, 0, 0, 0);
                using (var dc = new RenderTargetDrawingContext(target, _context))
                {
                    DrawingReplayer.Replay(frame, dc);
                    dc.Close();
                }
                target.EndDraw();
                var buffer = new byte[pixelWidth * pixelHeight * 4];
                Assert.Equal(JaliumResult.Ok, target.FetchReadback(buffer, (uint)(pixelWidth * 4), out _, out _));
                return buffer;
            });

            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormat.Bgra32);
            bitmap.Render(roundTripRoot);
            var pixels = new byte[pixelWidth * pixelHeight * 4];
            bitmap.CopyPixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), pixels, pixelWidth * 4, 0);
            int nonEmpty = 0;
            for (int i = 0; i < replayedPixels.Length; i += 4)
            {
                if (replayedPixels[i] != 0 || replayedPixels[i + 1] != 0 || replayedPixels[i + 2] != 0 || replayedPixels[i + 3] != 0)
                    nonEmpty++;
            }
            Assert.True(nonEmpty > (pixelWidth * pixelHeight) / 10, $"Expected non-empty render at {scale}x, got {nonEmpty} pixels.");

            var fullPath = Path.Combine(outputDir, fileName);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(fullPath))
            {
                encoder.Save(stream);
            }
            Assert.True(new FileInfo(fullPath).Length > 1024);
        }
    }

    private static (StackPanel Root, Dictionary<string, FrameworkElement> Probes) BuildCompositeScene()
    {
        var probes = new Dictionary<string, FrameworkElement>();
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root;box-sizing:border-box;width:320px;padding:12px;background:#0F172A;");

        // Band 1: Margin-collapsing with min-height clamp
        var band1 = new StackPanel();
        var band1Child = new Border();
        var band1Sibling = new Border();
        band1.Children.Add(band1Child);
        root.Children.Add(band1);
        root.Children.Add(band1Sibling);
        Css.SetStyle(band1, "display:block;min-height:44px;background:#1E293B;");
        Css.SetStyle(band1Child, "display:block;height:26px;margin-bottom:24px;background:#38BDF8;");
        Css.SetStyle(band1Sibling, "display:block;height:18px;margin-top:8px;margin-bottom:10px;background:#818CF8;");
        probes["band1"] = band1;
        probes["band1Child"] = band1Child;
        probes["band1Sibling"] = band1Sibling;

        // Band 2: Multi-line stretched FlexPanel with 50% child + spanning Grid
        var flex = new FlexPanel();
        var flexItem0 = new Border();
        var flexPercent = new Border { Child = new Border { Width = 20, Height = 16 } };
        flexItem0.Child = flexPercent;
        var flexItem1 = new Border();
        flex.Children.Add(flexItem0);
        flex.Children.Add(flexItem1);
        root.Children.Add(flex);
        Css.SetStyle(flex, "display:flex;flex-direction:row;flex-wrap:wrap;align-items:stretch;align-content:stretch;height:96px;margin-bottom:10px;background:#1E293B;");
        Css.SetStyle(flexItem0, "display:block;width:180px;height:auto;background:#334155;");
        Css.SetStyle(flexPercent, "display:block;height:50%;background:#34D399;");
        Css.SetStyle(flexItem1, "display:block;width:180px;height:28px;background:#F472B6;");
        probes["flex"] = flex;
        probes["flexItem0"] = flexItem0;
        probes["flexPercent"] = flexPercent;
        probes["flexItem1"] = flexItem1;

        // Band 3: Grid with spanning item over minmax(min-content, 60px) auto
        var grid = new Grid();
        var spanBox = new StackPanel();
        var spanA = new Border { Width = 75, Height = 20 };
        var spanB = new Border { Width = 75, Height = 20 };
        Css.SetStyle(spanA, "display:inline-block;background:#FBBF24;");
        Css.SetStyle(spanB, "display:inline-block;background:#F59E0B;");
        spanBox.Children.Add(spanA);
        spanBox.Children.Add(spanB);
        var cell0 = new Border { Height = 18 };
        var cell1 = new Border { Height = 18 };
        grid.Children.Add(spanBox);
        grid.Children.Add(cell0);
        grid.Children.Add(cell1);
        root.Children.Add(grid);
        Css.SetStyle(grid, "display:grid;justify-content:start;grid-template-columns:minmax(min-content, 60px) auto;margin-bottom:10px;background:#1E293B;");
        Css.SetStyle(spanBox, "display:block;grid-column:1 / span 2;");
        Css.SetStyle(cell0, "display:block;grid-column:1;background:#60A5FA;");
        Css.SetStyle(cell1, "display:block;grid-column:2;background:#A78BFA;");
        probes["grid"] = grid;
        probes["cell0"] = cell0;
        probes["cell1"] = cell1;

        // Band 4: Native Border host with indefinite-height 50% child and ::after absolute badge in padding-box
        var hostBody = new Border { Child = new Border { Width = 60, Height = 32 } };
        Css.SetClass(hostBody, "host-body");
        var nativeHost = new Border { Child = hostBody };
        Css.SetClass(nativeHost, "native-host");
        root.Children.Add(nativeHost);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            .native-host {
                position: relative;
                box-sizing: border-box;
                border: 6px solid #475569;
                padding: 8px;
                background: #1E293B;
            }
            .native-host > .host-body {
                display: block;
                height: 50%;
                background: #2DD4BF;
            }
            .native-host::after {
                content: '';
                position: absolute;
                right: 10px;
                bottom: 6px;
                width: 90px;
                max-width: 36px;
                height: 16px;
                background: #FB7185;
            }
            """));
        probes["nativeHost"] = nativeHost;
        probes["hostBody"] = hostBody;

        return (root, probes);
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue();
            root.Measure(new Size(width, height));
            var arrangeWidth = double.IsFinite(width) ? Math.Max(width, root.DesiredSize.Width) : root.DesiredSize.Width;
            var arrangeHeight = double.IsFinite(height) ? Math.Max(height, root.DesiredSize.Height) : root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, arrangeWidth, arrangeHeight));
            root.UpdateLayout();
        }
    }

    private static void Near(double expected, double actual) =>
        Assert.InRange(actual, expected - 0.06, expected + 0.06);
}
