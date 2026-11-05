using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssListMarkerTests
{
    [Theory]
    [InlineData("list-item", true)]
    [InlineData("inline list-item", true)]
    [InlineData("list-item flow-root block", true)]
    [InlineData("inline flow-root list-item", true)]
    [InlineData("flow list-item", true)]
    [InlineData("list-item flex", false)]
    [InlineData("list-item inline-grid", false)]
    [InlineData("grid inline list-item", false)]
    [InlineData("list-item list-item", false)]
    [InlineData("none list-item", false)]
    public void ListItemDisplayAcceptsOnlyTheFlowGrammar(string display, bool valid)
        => Assert.Equal(valid, CssAuthoring.IsValueSupported("display", display));

    [Theory]
    [InlineData(".item::marker", true)]
    [InlineData(".item:hover::marker", true)]
    [InlineData(".item:marker", false)]
    [InlineData(".item::marker:hover", false)]
    [InlineData(".item::marker > TextBlock", false)]
    [InlineData(":has(::marker)", false)]
    public void MarkerSelectorHasARealTerminalPseudoElement(string selector, bool valid)
        => Assert.Equal(valid, CssSelectorParser.ParseGroup(selector, out _) is not null);

    [Theory]
    [InlineData("list-style", "decimal inside", true)]
    [InlineData("list-style", "inside decimal", true)]
    [InlineData("list-style", "none none outside", true)]
    [InlineData("list-style", "disc none", true)]
    [InlineData("list-style", "none disc none", false)]
    [InlineData("list-style", "inside outside", false)]
    [InlineData("list-style", "decimal square", false)]
    [InlineData("list-style-type", "symbols(cyclic '甲' '乙')", true)]
    [InlineData("list-style-type", "'→ '", true)]
    [InlineData("list-style-type", "symbols(numeric '0')", false)]
    [InlineData("list-style-image", "none", true)]
    [InlineData("list-style-image", "url('marker.png')", true)]
    public void AuthoringReportsTheImplementedListGrammar(string property, string value, bool supported)
        => Assert.Equal(supported, CssAuthoring.IsValueSupported(property, value));

    [Theory]
    [InlineData("disc", "• ", "• ", "• ")]
    [InlineData("circle", "◦ ", "◦ ", "◦ ")]
    [InlineData("square", "▪ ", "▪ ", "▪ ")]
    [InlineData("decimal", "1. ", "2. ", "3. ")]
    [InlineData("upper-alpha", "A. ", "B. ", "C. ")]
    [InlineData("lower-roman", "i. ", "ii. ", "iii. ")]
    [InlineData("symbols(cyclic '甲' '乙')", "甲 ", "乙 ", "甲 ")]
    [InlineData("'→ '", "→ ", "→ ", "→ ")]
    public void NormalMarkersUseInheritedTypeAndImplicitListItemCounters(string type, string a, string b, string c)
    {
        var (root, rows) = List();
        Add(root, $".list {{ list-style-type: {type}; }} .item {{ display: list-item; }}");
        Layout(root);
        Assert.Equal(new[] { a, b, c }, rows.Select(row => Marker(row).Text));
        Assert.All(rows, row => Assert.Equal(Visibility.Visible, Marker(row).Visibility));
        Assert.False(CssEvaluationScheduler.HasPending(Dispatcher.CurrentDispatcher));
    }

    [Fact]
    public void NamedStylesSupplyPrefixSuffixAndUpdateWhenTheirDefinitionChanges()
    {
        var (root, rows) = List();
        var sheet = Add(root, """
            @counter-style steps { system: extends decimal; pad: 2 '0'; prefix: '['; suffix: '] '; }
            .item { display: list-item; list-style: steps; }
            """);
        Layout(root); Assert.Equal("[02] ", Marker(rows[1]).Text);
        Css.GetStyleSheets(root).Remove(sheet);
        Add(root, "@counter-style steps { symbols: '✓'; prefix: ''; suffix: '! '; } .item { display: list-item; list-style: steps; }");
        Layout(root); Assert.Equal("✓✓! ", Marker(rows[1]).Text);
    }

    [Theory]
    [InlineData("none", "1. ", "2. ", "3. ")]
    [InlineData("other 7", "1. ", "2. ", "3. ")]
    [InlineData("list-item 0", "0. ", "0. ", "0. ")]
    [InlineData("list-item 2", "2. ", "4. ", "6. ")]
    public void OnlyAnExplicitListItemIncrementSuppressesTheImplicitOne(string increment, string a, string b, string c)
    {
        var (root, rows) = List();
        Add(root, $".item {{ display: list-item; list-style: decimal; counter-increment: {increment}; }}");
        Layout(root); Assert.Equal(new[] { a, b, c }, rows.Select(row => Marker(row).Text));
    }

    [Fact]
    public void ResetAndSetSurroundTheImplicitIncrementInTheSpecifiedOrder()
    {
        var (root, rows) = List();
        Add(root, ".list { counter-reset: list-item 4; } .item { display: list-item; list-style: decimal; } .item:nth-child(2) { counter-set: list-item 9; }");
        Layout(root); Assert.Equal(new[] { "5. ", "9. ", "10. " }, rows.Select(row => Marker(row).Text));
    }

    [Theory]
    [InlineData("reversed(list-item)", "3. ", "2. ", "1. ")]
    [InlineData("reversed(list-item) 9", "8. ", "7. ", "6. ")]
    public void ReversedListItemsUseImplicitDecrementsAndAutomaticStarts(string reset, string a, string b, string c)
    {
        var (root, rows) = List();
        Add(root, $".list {{ counter-reset: {reset}; }} .item {{ display: list-item; list-style: decimal; }}");
        Layout(root); Assert.Equal(new[] { a, b, c }, rows.Select(row => Marker(row).Text));
        if (reset != "reversed(list-item)") return;
        rows[1].Visibility = Visibility.Collapsed; Layout(root);
        Assert.Equal("2. ", Marker(rows[0]).Text); Assert.Equal("1. ", Marker(rows[2]).Text);
        rows[1].Visibility = Visibility.Visible; Layout(root);
        Assert.Equal("3. ", Marker(rows[0]).Text);
    }

    [Fact]
    public void ReorderCollapseAndDisplaySwitchRebuildImplicitNumbering()
    {
        var (root, rows) = List();
        Add(root, ".item { display: list-item; list-style: decimal; }");
        Layout(root); var retained = Marker(rows[0]);
        root.Children.Remove(rows[2]); root.Children.Insert(0, rows[2]); Layout(root);
        Assert.Equal("1. ", Marker(rows[2]).Text); Assert.Equal("2. ", Marker(rows[0]).Text);
        Css.SetStyle(rows[0], "display: block;"); Layout(root);
        Assert.Equal(Visibility.Collapsed, retained.Visibility); Assert.Equal("2. ", Marker(rows[1]).Text);
        Assert.True(Assert.IsType<CssGeneratedContentValue>(retained.GetValue(CssGeneratedContent.ContentProperty)).IsNone);
        Css.SetStyle(rows[0], "display: list-item;"); Layout(root);
        Assert.Same(retained, Marker(rows[0])); Assert.Equal("2. ", retained.Text);
        rows[2].Visibility = Visibility.Collapsed; Layout(root); Assert.Equal("1. ", retained.Text);
    }

    [Fact]
    public void AutomaticMarkersRecoverAfterNoneAndShorthandRestoresInitialPosition()
    {
        var (root, rows) = List();
        Add(root, ".list { list-style: decimal inside; } .item { display: list-item; }");
        Layout(root); var marker = Marker(rows[0]);
        Css.SetStyle(root, "list-style-type: none;"); Layout(root);
        Assert.Equal(Visibility.Collapsed, marker.Visibility);
        Css.SetStyle(root, "list-style: circle;"); Layout(root);
        Assert.Same(marker, Marker(rows[0])); Assert.Equal(Visibility.Visible, marker.Visibility);
        Assert.Equal("◦ ", marker.Text); Assert.False(CssListProperties.Inside(rows[0]));
        Css.SetStyle(root, "list-style: none;"); Layout(root); Assert.Equal(Visibility.Collapsed, marker.Visibility);
        Css.SetStyle(root, "list-style: initial;"); Layout(root); Assert.Equal("• ", marker.Text);
    }

    [Fact]
    public void ExplicitContentOverridesNoneAndObservesOriginAttributesVariablesAndCounters()
    {
        var (root, rows) = List(); rows[0].Tag = "甲";
        Add(root, """
            .list { --label: '步骤'; }
            .item { display: list-item; list-style-type: none; }
            .item::marker { content: var(--label) counter(list-item) attr(Tag) ' ' / '编号' counter(list-item); }
            """);
        Layout(root); var marker = Marker(rows[0]);
        Assert.Equal("步骤1甲 ", marker.Text); Assert.Equal("编号1", marker.Alternative);
        Assert.Equal("编号1", UIElementAutomationPeer.CreatePeerForElement(marker)!.GetName());
        rows[0].Tag = "乙"; Css.SetStyle(root, "--label: '任务';"); Layout(root);
        Assert.Equal("任务1乙 ", marker.Text);
        rows[0].SetIsMouseOver(true);
        Add(root, ".item:hover::marker { content: none; }"); Layout(root);
        Assert.Equal(Visibility.Collapsed, marker.Visibility);
        rows[0].SetIsMouseOver(false); Layout(root); Assert.Equal(Visibility.Visible, marker.Visibility);
    }

    [Fact]
    public void MarkerNormalNoneAndNonListItemsHaveDifferentComputedContent()
    {
        var (root, rows) = List();
        Add(root, ".item { display: list-item; list-style: decimal; } .item::marker { content: 'x'; } .item:nth-child(1)::marker { content: normal; } .item:nth-child(2)::marker { content: none; }");
        Layout(root);
        Assert.Equal("1. ", Marker(rows[0]).Text); Assert.Equal(Visibility.Collapsed, Marker(rows[1]).Visibility);
        Assert.Equal("x", Marker(rows[2]).Text);
        Css.SetStyle(rows[2], "display: block;"); Layout(root);
        Assert.Equal(Visibility.Collapsed, Marker(rows[2]).Visibility);
        Assert.True(Assert.IsType<CssGeneratedContentValue>(Marker(rows[2]).GetValue(CssGeneratedContent.ContentProperty)).IsNone);
    }

    [Fact]
    public void MarkerUsesTextStylingButIgnoresPrincipalBoxPropertiesEvenAfterVariableSubstitution()
    {
        var (root, rows) = List();
        Add(root, """
            @layer base { .item::marker { color: green; } }
            .item { display: list-item; --ignored: 99px; color: red; text-transform: uppercase; }
            .item::marker { content: 'a  '; font-size: 24px; width: var(--ignored); height: 99px; margin: 99px; padding-inline: 99px; background-color: blue; display: none; position: fixed; counter-increment: list-item 99; }
            .item::after { content: counter(list-item); }
            """);
        Layout(root); var marker = Marker(rows[0]);
        Assert.True(double.IsNaN(marker.Width)); Assert.True(double.IsNaN(marker.Height));
        Assert.Equal(default, marker.Margin); Assert.Null(marker.Background);
        Assert.Equal(24, marker.FontSize); Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(marker.Foreground).Color);
        Assert.Equal(CssWhiteSpaceCollapse.Preserve, marker.GetValue(CssFlowProperties.WhiteSpaceProperty));
        Assert.Equal(CssTextTransformMode.None, marker.GetValue(CssFlowProperties.TextTransformProperty));
        Assert.Equal(Visibility.Visible, marker.Visibility);
        Assert.Equal("1", CssGeneratedContent.Get(rows[0])!.After!.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutsideMarkersLeaveContentWidthAndAlignAtItsFirstBaseline(bool rtl)
    {
        var (root, rows) = List(1); var body = Assert.IsType<TextBlock>(rows[0].Children[0]);
        Add(root, $$"""
            .item { display: list-item; list-style: decimal outside; padding: 6px 12px; font-size: 20px; direction: {{(rtl ? "rtl" : "ltr")}}; }
            .item::marker { font-size: 12px; }
            """);
        Layout(root); var marker = Marker(rows[0]);
        Assert.Equal(rtl ? rows[0].ActualWidth - 12 : 12, rtl ? marker.VisualBounds.X : marker.VisualBounds.Right, 5);
        Assert.Equal(12, body.VisualBounds.X, 5);
        Assert.Equal(body.VisualBounds.Y + CssFlowProperties.LineMetrics(body).Baseline,
            marker.VisualBounds.Y + CssFlowProperties.LineMetrics(marker).Baseline, 5);
        var measured = rows[0].DesiredSize;
        Css.SetStyle(rows[0], "list-style-type: none;"); Layout(root);
        Assert.Equal(measured.Width, rows[0].DesiredSize.Width, 5);
    }

    [Fact]
    public void InsideMarkersPrecedeBeforeAndChildrenWithoutChangingStructuralSelectors()
    {
        var (root, rows) = List(1); var body = rows[0].Children[0];
        Add(root, """
            .item { display: list-item; list-style: decimal inside; }
            .item::before { content: '前'; }
            .item > TextBlock:first-child:last-child { display: inline-block; width: 25px; }
            .item:has(> .fake) { width: 900px; }
            """);
        Layout(root); var marker = Marker(rows[0]); var before = CssGeneratedContent.Get(rows[0])!.Before!;
        Assert.Single(rows[0].Children); Assert.False(marker.Focusable);
        Assert.Equal(0, marker.VisualBounds.X, 5);
        Assert.Equal(marker.VisualBounds.Right, before.VisualBounds.X, 5);
        Assert.Equal(before.VisualBounds.Right, body.VisualBounds.X, 5);
        Assert.Equal(25, ((FrameworkElement)body).Width); Assert.True(double.IsNaN(rows[0].Width));
        Assert.Same(marker, new CssPanelChildren(rows[0])[0]);
    }

    [Fact]
    public void ChangingOnlyMarkerPositionInvalidatesBothFormattingPaths()
    {
        var (root, rows) = List(1); var body = rows[0].Children[0];
        Add(root, ".item { display: list-item; list-style: decimal inside; } .body { display: inline-block; }");
        Css.SetClass(body, "body"); Layout(root); var marker = Marker(rows[0]);
        Assert.Equal(marker.VisualBounds.Right, body.VisualBounds.X, 5);
        Css.SetStyle(rows[0], "list-style-position: outside;"); Layout(root);
        Assert.Equal(0, body.VisualBounds.X, 5); Assert.Equal(0, marker.VisualBounds.Right, 5);
        Css.SetStyle(rows[0], "list-style-position: inside;"); Layout(root);
        Assert.Equal(marker.VisualBounds.Right, body.VisualBounds.X, 5);
    }

    [Theory]
    [InlineData("inline list-item", true)]
    [InlineData("inline flow list-item", true)]
    [InlineData("inline flow-root list-item", false)]
    public void OutsideOnAnInlineFlowListItemUsesInsideFormatting(string display, bool inside)
    {
        var (root, rows) = List(1); var body = rows[0].Children[0]; Css.SetClass(body, "body");
        Add(root, $".list {{ display: block; }} .item {{ display: {display}; list-style: decimal outside; }} .body {{ display: inline-block; width: 40px; }}");
        Layout(root); var marker = Marker(rows[0]);
        Assert.Equal(inside, CssListProperties.Inside(rows[0]));
        Assert.Equal(inside ? marker.VisualBounds.Right : 0, body.VisualBounds.X, 5);
    }

    [Fact]
    public void MarkerAnimationsApplyTextValuesAndRejectBoxValues()
    {
        var (root, rows) = List(1);
        var sheet = Add(root, """
            .item { display: list-item; }
            @keyframes marker-text { from { color: red; font-size: 12px; width: 80px; margin: 90px; transform: translateX(50px); } to { color: blue; font-size: 24px; width: 100px; } }
            """);
        Layout(root);
        var frames = CssKeyframes.EvaluateFrames(CssNode.Get(Marker(rows[0])), Assert.Single(sheet.Keyframes));
        Assert.All(frames, frame =>
        {
            Assert.DoesNotContain(FrameworkElement.WidthProperty, frame.Values.Keys);
            Assert.DoesNotContain(FrameworkElement.MarginProperty, frame.Values.Keys);
            Assert.DoesNotContain(UIElement.RenderTransformProperty, frame.Values.Keys);
            Assert.Contains(TextBlock.FontSizeProperty, frame.Values.Keys);
            Assert.Contains(TextBlock.ForegroundProperty, frame.Values.Keys);
        });
    }

    [Fact]
    public void AnimatedDisplayChangesCreateAndSuppressRetainedMarkers()
    {
        var (root, rows) = List(1); Add(root, ".item { display: block; list-style: decimal; }"); Layout(root);
        Assert.Null(CssGeneratedContent.Get(rows[0]));
        rows[0].SetAnimatedValue(CssDisplayProperties.SpecificationProperty, new CssDisplaySpecification(CssDisplayMode.Block, ListItem: true), false);
        Layout(root); var marker = Marker(rows[0]); Assert.Equal("1. ", marker.Text);
        rows[0].ClearAnimatedValue(CssDisplayProperties.SpecificationProperty); Layout(root);
        Assert.Equal(Visibility.Collapsed, marker.Visibility);
    }

    [Fact]
    public void MarkersNeverEnterTabOrderAndDynamicLabelsPreserveFocus()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var (root, rows) = List(2); var a = new Button { Content = "第一项" }; var b = new Button { Content = "第二项" };
        foreach (var row in rows) row.Children.Clear(); rows[0].Children.Add(a); rows[1].Children.Add(b);
        Add(root, ".list { list-style: decimal; } .item { display: list-item; }");
        var window = new DisplayedTestWindow { Width = 360, Height = 240, Content = root, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Css.SetStyle(root, "list-style-type: upper-roman;"); Layout(window);
            Assert.Equal("II. ", Marker(rows[1]).Text); Assert.Same(b, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
            Assert.All(rows, row => Assert.False(Marker(row).Focusable));
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void ListMetadataAndSupportsUseTheRuntimeRegistry()
    {
        var (root, rows) = List(1);
        Add(root, "@supports (display: list-item) and (list-style: decimal inside) and selector(.item::marker) { .item { display: list-item; list-style: decimal; } }");
        Layout(root); Assert.Equal("1. ", Marker(rows[0]).Text);
        Assert.Equal("disc", CssAuthoring.GetProperty("list-style-type").InitialValue);
        Assert.Equal("outside", CssAuthoring.GetProperty("list-style-position").InitialValue);
        Assert.True(CssAuthoring.GetProperty("list-style").IsShorthand);
        Assert.True(CssAuthoring.GetProperty("list-style").IsInherited);
        Assert.True(CssAuthoring.GetProperty("list-style-type").IsInherited);
        Assert.All(CssAuthoring.GetValues("list-style"), value => Assert.True(Css.Supports("list-style", value)));
    }

    [Theory]
    [InlineData("revert", false)]
    [InlineData("revert-layer", false)]
    [InlineData("var(--missing, revert)", false)]
    [InlineData("unset", true)]
    [InlineData("inherit", true)]
    public void MarkerDefaultsSurviveOriginRollbackButCanBeExplicitlyInherited(string keyword, bool inherits)
    {
        var (root, rows) = List(1);
        Add(root, $$"""
            .item { display: list-item; text-transform: uppercase; white-space: normal; --rollback: revert; }
            .item::marker { content: 'a '; text-transform: {{keyword}}; white-space: {{keyword}}; }
            """);
        Layout(root); var marker = Marker(rows[0]);
        Assert.Equal(inherits ? CssTextTransformMode.Uppercase : CssTextTransformMode.None, marker.GetValue(CssFlowProperties.TextTransformProperty));
        Assert.Equal(inherits ? CssWhiteSpaceCollapse.Collapse : CssWhiteSpaceCollapse.Preserve, marker.GetValue(CssFlowProperties.WhiteSpaceProperty));
    }

    [Fact]
    public void ClippingSuppressesOutsideMarkerHitWhileLeavingItsCounterActive()
    {
        var (root, rows) = List(2);
        Add(root, ".list { display: block; padding-left: 50px; } .item { display: list-item; list-style: decimal outside; }");
        Layout(root); var marker = Marker(rows[0]);
        var point = new Point(rows[0].VisualBounds.X + marker.VisualBounds.X + marker.ActualWidth / 2,
            rows[0].VisualBounds.Y + marker.VisualBounds.Y + marker.ActualHeight / 2);
        Assert.Same(rows[0], root.InputHitTest(point));
        rows[0].ClipToBounds = true; Layout(root);
        Assert.NotSame(rows[0], root.InputHitTest(point)); Assert.Equal("2. ", Marker(rows[1]).Text);
        Assert.NotSame(rows[0], root.HitTest(point)?.VisualHit);
        DependencyObject? filtered = null;
        VisualTreeHelper.HitTest(root, null, result => { filtered = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        Assert.NotSame(marker, filtered);
        rows[0].ClipToBounds = false; Layout(root); Assert.Same(rows[0], root.InputHitTest(point));
    }

    [Fact]
    public void OutsideMarkerHitTargetsTheOriginAndStaysOutOfDocumentChildren()
    {
        var (root, rows) = List(1);
        Add(root, ".list { padding: 0 50px; } .item { display: list-item; list-style: decimal outside; } .item::marker { content: '编号 '; }");
        Layout(root); var marker = Marker(rows[0]);
        var point = new Point(rows[0].VisualBounds.X + marker.VisualBounds.X + marker.ActualWidth / 2,
            rows[0].VisualBounds.Y + marker.VisualBounds.Y + marker.ActualHeight / 2);
        Assert.Same(rows[0], root.HitTest(point)?.VisualHit); Assert.Same(rows[0], root.InputHitTest(point));
        Assert.Single(rows[0].Children); Assert.Single(CssMatcher.Children(CssNode.Get(rows[0])));
        Assert.DoesNotContain(marker, new CssPanelChildren(rows[0]).Enumerate());
    }

    [Fact]
    public void MarkerSeesListItemBeforeBeforeContentAndNestedScopesKeepTheirIdentity()
    {
        var (root, rows) = List(2); var nested = new StackPanel(); Css.SetClass(nested, "list"); rows[0].Children.Add(nested);
        var child = Item(); nested.Children.Add(child);
        Add(root, """
            .list { counter-reset: list-item; }
            .item { display: list-item; }
            .item::marker { content: counters(list-item, '.') ' '; }
            .item::before { counter-increment: list-item 10; content: counter(list-item); }
            """);
        Layout(root);
        Assert.Equal("1 ", Marker(rows[0]).Text); Assert.Equal("11", CssGeneratedContent.Get(rows[0])!.Before!.Text);
        Assert.Equal("11.1 ", Marker(child).Text); Assert.Equal("12 ", Marker(rows[1]).Text);
    }

    [Fact]
    public void FlexBlockificationPreservesTheListItemFlagAndStyleQueriesSeeIt()
    {
        var (root, rows) = List(1);
        Add(root, ".list { display: flex; } .item { display: inline flow-root list-item; list-style: decimal; }");
        Layout(root);
        var computed = CssDisplayProperties.Computed(CssNode.Get(rows[0]));
        Assert.True(computed.ListItem); Assert.False(computed.Inline);
        Assert.True(CssDisplayStyleQuery.Matches("flow-root list-item", CssNode.Get(rows[0]), CssLengthContext.Default));
        Assert.False(CssDisplayStyleQuery.Matches("flow-root", CssNode.Get(rows[0]), CssLengthContext.Default));
        Assert.Equal("1. ", Marker(rows[0]).Text);
    }

    [Fact]
    public void CompiledBuilderKeepsMarkerTargetAndListDeclarations()
    {
        var builder = new CssStyleSheetBuilder(1);
        var compound = builder.Compound("StackPanel", false, null, false, true, false, null, ["item"], null, 0, null, null, (byte)CssPseudoElement.Marker);
        builder.Rule([builder.Selector([compound], [], (1 << 10) + 2, 0, 0, false, false)],
            [builder.Declaration("content", "'编译' counter(list-item)", false)], -1, null, -1);
        var (root, rows) = List(1); Add(root, ".item { display: list-item; }"); Css.GetStyleSheets(root).Add(builder.Build());
        Layout(root); Assert.Equal("编译1", Marker(rows[0]).Text);
    }

    private static (StackPanel Root, StackPanel[] Items) List(int count = 3)
    {
        var root = new StackPanel(); Css.SetClass(root, "list");
        var rows = Enumerable.Range(0, count).Select(_ => Item()).ToArray();
        foreach (var row in rows) root.Children.Add(row);
        return (root, rows);
    }
    private static StackPanel Item()
    {
        var item = new StackPanel(); Css.SetClass(item, "item");
        item.Children.Add(new TextBlock { Text = "列表正文" }); return item;
    }
    private static CssGeneratedMarker Marker(Panel host) => Assert.IsType<CssGeneratedMarker>(CssGeneratedContent.Get(host)?.Marker);
    private static CssStyleSheet Add(FrameworkElement host, string css)
    {
        var sheet = CssStyleSheet.Parse(css); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(host).Add(sheet); return sheet;
    }
    private static void Layout(FrameworkElement root, int width = 360)
    {
        var viewport = new Size(width, 400);
        Css.SetViewportMetrics(root, new CssViewportMetrics(viewport, viewport, viewport));
        for (var i = 0; i < 5; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(viewport); root.Arrange(new Rect(0, 0, width, 400)); root.UpdateLayout();
        }
    }
}
