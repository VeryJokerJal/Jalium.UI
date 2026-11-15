using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentListMarkerTests
{
    [Theory]
    [InlineData(TextMarkerStyle.Decimal, "3. ", "4. ", "5. ")]
    [InlineData(TextMarkerStyle.UpperRoman, "III. ", "IV. ", "V. ")]
    [InlineData(TextMarkerStyle.LowerLatin, "c. ", "d. ", "e. ")]
    [InlineData(TextMarkerStyle.None, null, null, null)]
    public void NativeMarkersFollowTheirStyleAndStartingNumber(TextMarkerStyle style, string? first, string? second, string? third)
    {
        using var f = new Fixture(); f.List.MarkerStyle = style; f.Layout();
        Assert.Equal(new[] { first, second, third }.Where(value => value is not null), f.VisibleMarkers().Select(marker => marker.Text));
        f.OriginalSource();
    }

    [Theory]
    [InlineData("lower-alpha", "c. ", "d. ", "e. ")]
    [InlineData("decimal-leading-zero", "03. ", "04. ", "05. ")]
    [InlineData("'步骤 '", "步骤 ", "步骤 ", "步骤 ")]
    public void AuthoredListStylesOverrideTheNativeFallback(string style, string first, string second, string third)
    {
        using var f = new Fixture(); Css.SetStyle(f.List, $"list-style-type:{style};"); f.Layout();
        Assert.Equal(new[] { first, second, third }, f.VisibleMarkers().Select(marker => marker.Text));
    }

    [Fact]
    public void MarkerPseudoRulesUseTheOriginalItemAndSharedCounterTree()
    {
        using var f = new Fixture(); Css.SetStyleSheet(f.Document,
            "ListItem::marker{content:'[' counter(list-item) '] ';color:blue;font-weight:700;font-size:21px;}ListItem:nth-child(2)::marker{content:'中 ';}");
        f.Layout(); var markers = f.VisibleMarkers();
        Assert.Equal(new[] { "[3] ", "中 ", "[5] " }, markers.Select(marker => marker.Text));
        foreach (var marker in markers)
        {
            Assert.Equal(21, marker.FontSize); Assert.Equal(FontWeights.Bold, marker.FontWeight);
            Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(marker.Foreground).Color);
        }
        Assert.All(markers, marker => Assert.Contains(marker.Origin.Target, f.Items)); f.OriginalSource();
    }

    [Theory]
    [InlineData(true, "3. ", "4. ")]
    [InlineData(false, "3. ", "5. ")]
    public void DisplayNoneSkipsCounterIncrementsAndVisibilityHiddenRetainsThem(bool displayNone, string first, string last)
    {
        using var f = new Fixture(); Css.SetStyle(f.Items[1], displayNone ? "display:none;" : "visibility:hidden;"); f.Layout();
        Assert.Equal(new[] { first, last }, f.VisibleMarkers().Select(marker => marker.Text));
        Assert.Equal(displayNone ? "第一项\n第三项\n" : "第一项\n第二项\n第三项\n", f.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyBoxGeneratingItemsContributeToTheOutsideMarkerColumn(bool displayNone)
    {
        using var f = new Fixture();
        Css.SetStyleSheet(f.Document, "ListItem:first-child::marker{content:'very long marker ';}");
        Css.SetStyle(f.Items[0], displayNone ? "display:none;" : "visibility:hidden;"); f.Layout();
        var hiddenMarker = Fixture.Markers(f.Body).Single(marker => ReferenceEquals(marker.Origin.Target, f.Items[0]));
        var expected = displayNone ? 20 : hiddenMarker.DesiredSize.Width;
        Assert.Equal(expected, f.Body.Paragraphs[1].TransformToAncestor(f.Body).X, 3);
        Css.SetStyle(f.Items[0], ""); f.Layout();
        Assert.True(f.Body.Paragraphs[1].TransformToAncestor(f.Body).X > 20);
    }

    [Fact]
    public void NestedListsHaveIndependentNativeStartsAndCanReadAncestorCounters()
    {
        using var f = new Fixture(); var nested = new DocList { StartIndex = 7, MarkerStyle = TextMarkerStyle.UpperRoman };
        var item = new ListItem(new Paragraph(new Run("内项"))); nested.ListItems.Add(item); f.Items[0].Blocks.Add(nested);
        Css.SetStyle(item, "list-style-type:decimal;"); Css.SetStyleSheet(f.Document, "ListItem ListItem::marker{content:counters(list-item,'.') ' ';}");
        f.Layout();
        Assert.Equal(new[] { "3. ", "3.7 ", "4. ", "5. " }, f.VisibleMarkers().Select(marker => marker.Text));
    }

    [Fact]
    public void AuthorCounterResetSetAndIncrementUseTheExistingCascade()
    {
        using var f = new Fixture(); Css.SetStyle(f.List, "counter-reset:list-item 10;");
        Css.SetStyle(f.Items[0], "counter-increment:list-item 2;"); Css.SetStyle(f.Items[1], "counter-set:list-item 20;"); f.Layout();
        Assert.Equal(new[] { "12. ", "20. ", "21. " }, f.VisibleMarkers().Select(marker => marker.Text));
    }

    [Fact]
    public void TwoViewsKeepSeparateMarkerVisualsWithoutDuplicatingCounters()
    {
        using var f = new Fixture(); f.Layout(); var first = f.VisibleMarkers(); var second = Fixture.Markers(f.Other).Where(marker => marker.Visibility == Visibility.Visible).ToArray();
        Assert.Equal(new[] { "3. ", "4. ", "5. " }, first.Select(marker => marker.Text));
        Assert.Equal(first.Select(marker => marker.Text), second.Select(marker => marker.Text));
        Assert.All(first.Zip(second), pair => Assert.NotSame(pair.First, pair.Second));
        f.Body.Dispose(); Fixture.Layout(f.Other, 180); Assert.Equal(new[] { "3. ", "4. ", "5. " }, Fixture.Markers(f.Other).Select(marker => marker.Text));
        f.OriginalSource();
    }

    [Fact]
    public void StartingNumberStyleInsertionAndRemovalRefreshTheCurrentMarkers()
    {
        using var f = new Fixture(); f.Layout(); f.List.StartIndex = 9; f.List.MarkerStyle = TextMarkerStyle.UpperRoman; f.Layout();
        Assert.Equal(new[] { "IX. ", "X. ", "XI. " }, f.VisibleMarkers().Select(marker => marker.Text));
        var inserted = new ListItem(new Paragraph(new Run("新增"))); f.List.ListItems.Insert(1, inserted); f.Layout();
        Assert.Equal(new[] { "IX. ", "X. ", "XI. ", "XII. " }, f.VisibleMarkers().Select(marker => marker.Text));
        f.List.ListItems.Remove(inserted); Css.SetStyle(f.List, "list-style:none;"); f.Layout(); Assert.Empty(f.VisibleMarkers());
        Css.SetStyle(f.List, ""); f.Layout(); Assert.Equal(new[] { "IX. ", "X. ", "XI. " }, f.VisibleMarkers().Select(marker => marker.Text));
    }

    [Theory]
    [InlineData("block")]
    [InlineData("grid")]
    [InlineData("flex")]
    [InlineData("contents")]
    public void ConvertedDocumentBoxesKeepTheSameSourceMarker(string display)
    {
        using var f = new Fixture(); Css.SetStyle(f.Document, $"display:{display};grid-template-columns:1fr;"); f.Layout();
        Assert.Equal(new[] { "3. ", "4. ", "5. " }, f.VisibleMarkers().Select(marker => marker.Text)); f.OriginalSource();
    }

    [Theory]
    [InlineData("flex;flex-direction:column")]
    [InlineData("grid;grid-template-columns:1fr")]
    public void AnAuthoredListFormatterKeepsItsOriginalItemsAndTheirOrder(string display)
    {
        using var f = new Fixture(); Css.SetStyle(f.List, $"display:{display};padding-inline:40px;");
        Css.SetStyle(f.Items[0], "order:2;"); Css.SetStyle(f.Items[1], "order:0;"); Css.SetStyle(f.Items[2], "order:1;"); f.Layout();
        var y = f.Body.Paragraphs.Select(text => text.TransformToAncestor(f.Body).Y).ToArray();
        Assert.True(y[1] < y[2] && y[2] < y[0]);
        Assert.Equal(new[] { "3. ", "4. ", "5. " }, f.VisibleMarkers().Select(marker => marker.Text));
        Assert.All(f.VisibleMarkers(), marker => Assert.True(marker.TransformToAncestor(f.Body).X >= 0));
        f.OriginalSource();
    }

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void OutsideMarkersAlignWithTheFirstFormattedLineAndStayOutsideContent(string direction)
    {
        using var f = new Fixture(); Css.SetStyle(f.Document, $"direction:{direction};"); f.Layout();
        var marker = f.VisibleMarkers()[0]; var bounds = marker.TransformToAncestor(f.Body);
        var text = f.Body.Paragraphs[0]; var textBounds = text.TransformToAncestor(f.Body);
        Assert.Equal(textBounds.Y + text.GetFirstBaselineOffset(text.RenderSize.Height), bounds.Y + marker.GetLastBaselineOffset(marker.RenderSize.Height), 3);
        if (direction == "ltr") Assert.True(bounds.X + marker.RenderSize.Width <= textBounds.X + .01);
        else Assert.True(bounds.X >= textBounds.X + text.RenderSize.Width - .01);
    }

    [Fact]
    public void CustomCounterStylesUseTheirDeclaredPrefixAndSuffix()
    {
        using var f = new Fixture(); Css.SetStyleSheet(f.Document, "@counter-style stage{system:extends decimal;prefix:'第';suffix:'步 ';}List{list-style-type:stage;}"); f.Layout();
        Assert.Equal(new[] { "第3步 ", "第4步 ", "第5步 " }, f.VisibleMarkers().Select(marker => marker.Text));
    }

    [Fact]
    public void MarkerTypographyChangesReflowAndRepaintWithoutChangingCounterText()
    {
        using var f = new Fixture(); Css.SetStyleSheet(f.Document, "ListItem::marker{font-size:16px;color:green;}"); f.Layout();
        var before = f.Body.Paragraphs[0].TransformToAncestor(f.Body).X;
        Css.SetStyleSheet(f.Document, "ListItem::marker{font-size:32px;color:red;letter-spacing:2px;}"); f.Layout();
        Assert.Equal(new[] { "3. ", "4. ", "5. " }, f.VisibleMarkers().Select(marker => marker.Text));
        Assert.All(f.VisibleMarkers(), marker => { Assert.Equal(32, marker.FontSize); Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(marker.Foreground).Color); });
        Assert.True(f.Body.Paragraphs[0].TransformToAncestor(f.Body).X > before + 10);
    }

    [Theory]
    [InlineData("ltr", false)]
    [InlineData("rtl", false)]
    [InlineData("ltr", true)]
    [InlineData("rtl", true)]
    public void ExplicitMarkerOffsetAddsTheRequestedOutsideGap(string direction, bool converted)
    {
        using var f = new Fixture(); Css.SetStyle(f.Document, $"direction:{direction};" + (converted ? "display:grid;grid-template-columns:1fr;" : ""));
        f.List.MarkerOffset = 12; f.Layout();
        var marker = f.VisibleMarkers()[0]; var m = marker.TransformToAncestor(f.Body); var text = f.Body.Paragraphs[0]; var t = text.TransformToAncestor(f.Body);
        Assert.Equal(12, direction == "ltr" ? t.X - m.X - marker.RenderSize.Width : m.X - t.X - text.RenderSize.Width, 3);
    }

    [Fact]
    public void EmptyItemsPaintMarkersAndReserveARealLine()
    {
        using var f = new Fixture(); f.Items[0].Blocks.Clear(); f.Layout();
        var markers = f.VisibleMarkers(); Assert.Equal(new[] { "3. ", "4. ", "5. " }, markers.Select(marker => marker.Text));
        Assert.True(markers[0].RenderSize.Width > 0 && markers[0].RenderSize.Height > 0);
        Assert.True(markers[1].TransformToAncestor(f.Body).Y >= markers[0].TransformToAncestor(f.Body).Y + 20);
        Assert.Equal("第二项\n第三项\n", f.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsideMarkersFlowWithParagraphTextAndKeepSourceOffsets(bool rtl)
    {
        var run = new Run("内容"); var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph) { PagePadding = new(0), FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
        Css.SetStyle(paragraph, $"display:list-item;list-style-type:decimal;list-style-position:inside;direction:{(rtl ? "rtl" : "ltr")};");
        using var body = new FlowDocumentPresentation(); body.SetDocument(document); Fixture.Layout(body, 240);
        var marker = Assert.Single(Fixture.Markers(body)); Assert.Equal("1. ", marker.Text);
        var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        Assert.Equal("内容\n", provider.DocumentRange.GetText(-1));
        var bounds = provider.DocumentRange.FindText("内容", false, false)!.GetBoundingRectangles(); Assert.NotEmpty(bounds);
        var at = marker.TransformToAncestor(body);
        Assert.True(rtl ? at.X >= bounds[0] + bounds[2] - .1 : at.X + marker.RenderSize.Width <= bounds[0] + .1);
        Assert.Same(paragraph, run.Parent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedMarkerImagesReuseTheExistingImagePipelineAndTextFallback(bool converted)
    {
        using var f = new Fixture();
        if (converted) Css.SetStyle(f.Document, "display:grid;grid-template-columns:1fr;");
        Css.SetStyle(f.List, "list-style-image:linear-gradient(red,blue);"); f.Layout();
        Assert.All(f.VisibleMarkers(), marker =>
        {
            Assert.True(marker.HasImageContent); var at = marker.TransformToAncestor(f.Body);
            Assert.True(double.IsFinite(at.X) && double.IsFinite(at.Y));
            Assert.True(marker.RenderSize.Width > 0 && marker.RenderSize.Height > 0);
        });
        Css.SetStyle(f.List, "list-style-image:url('data:image/png;base64,AA==');"); f.Layout();
        Assert.All(f.VisibleMarkers(), marker => Assert.False(marker.HasImageContent));
        Assert.Equal(new[] { "3. ", "4. ", "5. " }, f.VisibleMarkers().Select(marker => marker.Text));
    }

    [Theory]
    [InlineData(26, "26. ", "27. ", "28. ")]
    [InlineData(2147483646, "2147483646. ", "2147483647. ", "2147483647. ")]
    public void LargeNativeStartsUseTheCounterLimitAndReserveMarkerWidth(int start, string first, string second, string third)
    {
        using var f = new Fixture(); f.List.StartIndex = start; f.Layout();
        Assert.Equal(new[] { first, second, third }, f.VisibleMarkers().Select(marker => marker.Text));
        Assert.True(f.VisibleMarkers()[0].TransformToAncestor(f.Body).X >= -.01);
    }

    [Fact]
    public void OrdinaryMarkerMeasurementDoesNotAddStandaloneTextOpticalSlackToRows()
    {
        using var f = new Fixture(); Css.SetStyleSheet(f.Document, "Paragraph{margin:0;}"); f.Layout(); Assert.Equal(82, f.Body.ExtentHeight, 3);
    }

    [Fact]
    public void EmptyItemAndMarkerLeadingStayStableAfterCounterTextMutation()
    {
        using var f = new Fixture(); f.Items[0].Blocks.Clear(); f.Document.FontFamily = "Helvetica Neue"; f.Document.FontSize = 20; f.Document.LineHeight = 32;
        Css.SetStyle(f.Document, "font-family:'Helvetica Neue';font-size:20px;line-height:32px;"); f.Layout();
        double height = f.Body.ExtentHeight; Assert.All(f.VisibleMarkers(), marker => Assert.Equal(32, marker.DesiredSize.Height, 3));
        f.List.StartIndex = 11; f.Layout(); Assert.Equal(height, f.Body.ExtentHeight, 3);
        Assert.All(f.VisibleMarkers(), marker => Assert.Equal(32, marker.DesiredSize.Height, 3));
        f.List.StartIndex = 3; f.Layout(); Assert.Equal(height, f.Body.ExtentHeight, 3);
    }

    [Theory]
    [InlineData("columns:2;column-gap:12px;height:48px;column-fill:auto;")]
    [InlineData("display:grid;grid-template-columns:1fr;")]
    public void MarkersFollowTheirOriginalFirstLineAfterColumnOrBoxLayout(string style)
    {
        using var f = new Fixture(); Css.SetStyle(f.Document, style); f.Layout();
        var markers = f.VisibleMarkers(); Assert.Equal(new[] { "3. ", "4. ", "5. " }, markers.Select(marker => marker.Text));
        for (int i = 0; i < markers.Length; i++)
        {
            var marker = markers[i]; var at = marker.TransformToAncestor(f.Body);
            var provider = (ITextProvider)f.Body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
            var bounds = provider.DocumentRange.FindText(new[] { "第一项", "第二项", "第三项" }[i], false, false)!.GetBoundingRectangles();
            Assert.NotEmpty(bounds); Assert.True(at.X + marker.RenderSize.Width <= bounds[0] + .1);
            Assert.True(Math.Abs(at.Y - bounds[1]) < 10, $"Marker {i} did not follow its first line");
        }
    }

    [Fact]
    public void RemovingAListOrReplacingTheDocumentReleasesItsMarkerVisuals()
    {
        using var f = new Fixture(); f.Layout(); var old = f.VisibleMarkers();
        f.Document.Blocks.Clear(); f.Layout(); Assert.Empty(Fixture.Markers(f.Body)); Assert.All(old, marker => Assert.Null(marker.VisualParent));
        var replacement = new FlowDocument(new Paragraph(new Run("new"))); f.Body.SetDocument(replacement); Fixture.Layout(f.Body, 240);
        Assert.Empty(Fixture.Markers(f.Body)); Assert.Same(replacement, f.Body.Document);
    }

    [Theory]
    [InlineData(false, 107)]
    [InlineData(true, 207)]
    public void ConvertedItemFramesKeepTheNativeMarkerColumnAndPercentageBasis(bool rtl, double left)
    {
        var paragraph = new Paragraph(new Run("原文")); var item = new ListItem(paragraph); var list = new DocList(item);
        var document = new FlowDocument(list) { PagePadding = new(0), FontFamily = "Courier New", FontSize = 16, LineHeight = 24, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        Css.SetStyle(list, "padding:10%;"); Css.SetStyle(item, "width:50%;margin:5%;padding:10%;border:2px solid red;");
        Css.SetStyle(document, "display:grid;grid-template-columns:1fr;");
        using var body = new FlowDocumentPresentation(); body.SetDocument(document); Fixture.Layout(body, 400);
        var paragraphBox = Assert.Single(Descendants(body).OfType<FrameworkElement>(), element =>
            element is ICssStyledBox styled && ReferenceEquals(styled.StyleSource, paragraph));
        Assert.Equal(left, paragraphBox.TransformToAncestor(body).X, 2); Assert.Equal(86, paragraphBox.ActualWidth, 2);
        Assert.Same(item, paragraph.Parent); Assert.Same(list, item.Parent);

        static IEnumerable<Visual> Descendants(Visual root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (VisualTreeHelper.GetChild(root, i) is Visual child)
                { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ListItem[] Items = [new(new Paragraph(new Run("第一项"))), new(new Paragraph(new Run("第二项"))), new(new Paragraph(new Run("第三项")))];
        internal readonly DocList List = new() { StartIndex = 3, MarkerStyle = TextMarkerStyle.Decimal };
        internal readonly FlowDocument Document;
        internal readonly FlowDocumentPresentation Body = new(), Other = new();
        internal string Text => ((ITextProvider)Body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).DocumentRange.GetText(-1);
        internal Fixture()
        {
            foreach (var item in Items) List.ListItems.Add(item);
            Document = new(List) { PagePadding = new(0), FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
            Css.SetStyle(Document, "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;");
            Body.SetDocument(Document); Other.SetDocument(Document);
        }
        internal void Layout() { Layout(Body, 240); Layout(Other, 180); }
        internal static void Layout(FlowDocumentPresentation body, double width)
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(width, double.PositiveInfinity)); body.Arrange(new(0, 0, width, 600)); }
        }
        internal CssGeneratedText[] VisibleMarkers() => Markers(Body).Where(marker => marker.Visibility == Visibility.Visible && CssTableVisibility.Visible(marker.Origin.Target)).ToArray();
        internal static IEnumerable<CssGeneratedText> Markers(Visual root)
        {
            if (root is CssGeneratedText { Kind: CssPseudoElement.Marker } marker) yield return marker;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (VisualTreeHelper.GetChild(root, i) is Visual child) foreach (var descendant in Markers(child)) yield return descendant;
        }
        internal void OriginalSource()
        {
            Assert.Same(List, Assert.Single(Document.Blocks)); Assert.Equal(Items, List.ListItems);
            Assert.All(Items, item => Assert.Same(List, item.Parent));
            Assert.DoesNotContain(CssNode.Get(Document).EnumerateChildren(), child => child.Target is CssGeneratedText);
        }
        public void Dispose() { Body.Dispose(); Other.Dispose(); }
    }
}
