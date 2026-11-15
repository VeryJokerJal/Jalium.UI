using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed partial class CssRubyInterCharacterTests
{
    [Theory]
    [InlineData("horizontal-tb")]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void AnnotationComputedModeIsForcedWhileItsAuthoredModeSurvives(string authored)
    {
        var ruby = new StackPanel(); var note = Text("ㄓㄨ", 16); ruby.Children.Add(note);
        Css.SetStyle(ruby, "display:ruby;ruby-position:inter-character;writing-mode:horizontal-tb;");
        Css.SetStyle(note, $"display:ruby-text;writing-mode:{authored};"); Flush();
        Assert.Equal(CssWritingMode.VerticalRl, note.GetValue(CssWritingModeProperties.WritingModeProperty));
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(note));
        Css.SetStyle(ruby, "display:ruby;ruby-position:over;writing-mode:horizontal-tb;"); Flush();
        Assert.Equal(Enum.Parse<CssWritingMode>(authored.Replace("-", ""), true), CssWritingModeProperties.GetWritingMode(note));
    }

    [Fact]
    public void ContainerModeStaysHorizontalAndDescendantsInheritTheForcedAnnotationMode()
    {
        var ruby = new StackPanel(); var rtc = new StackPanel(); var rt = new StackPanel();
        var child = Text("注音", 16); var own = Text("水平", 16); rt.Children.Add(child); rt.Children.Add(own); rtc.Children.Add(rt); ruby.Children.Add(rtc);
        Css.SetStyle(ruby, "display:ruby;ruby-position:inter-character;writing-mode:horizontal-tb;");
        Css.SetStyle(rtc, "display:ruby-text-container;"); Css.SetStyle(rt, "display:ruby-text;writing-mode:horizontal-tb;");
        Css.SetStyle(child, "display:inline;"); Css.SetStyle(own, "display:inline;writing-mode:horizontal-tb;"); Flush();
        Assert.Equal(CssWritingMode.HorizontalTb, rtc.GetValue(CssWritingModeProperties.WritingModeProperty));
        Assert.Equal(CssWritingMode.VerticalRl, rt.GetValue(CssWritingModeProperties.WritingModeProperty));
        Assert.Equal(CssWritingMode.VerticalRl, child.GetValue(CssWritingModeProperties.WritingModeProperty));
        Assert.Equal(CssWritingMode.HorizontalTb, own.GetValue(CssWritingModeProperties.WritingModeProperty));
        Assert.Same(rt, child.VisualParent); Assert.Same(rtc, rt.VisualParent);
    }

    [Fact]
    public void ALocalTwoWayWritingModeBindingIsPreservedAcrossForcingAndRestoration()
    {
        var ruby = new StackPanel(); var note = Text("ㄓㄨ", 16); ruby.Children.Add(note); var source = new ModeSource();
        note.SetBinding(CssWritingModeProperties.WritingModeProperty, new Binding(nameof(ModeSource.Mode)) { Source = source, Mode = BindingMode.TwoWay });
        var expression = BindingOperations.GetBindingExpression(note, CssWritingModeProperties.WritingModeProperty);
        Assert.NotNull(expression);
        Css.SetStyle(note, "display:ruby-text;"); Css.SetStyle(ruby, "display:ruby;ruby-position:inter-character;"); Flush();
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(note));
        Assert.Equal(CssWritingMode.HorizontalTb, source.Mode); Assert.Same(expression, BindingOperations.GetBindingExpression(note, CssWritingModeProperties.WritingModeProperty));
        source.Mode = CssWritingMode.VerticalLr; Flush();
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(note)); Assert.Equal(CssWritingMode.VerticalLr, source.Mode);
        Css.SetStyle(ruby, "display:ruby;ruby-position:over;"); Flush();
        Assert.Equal(CssWritingMode.VerticalLr, CssWritingModeProperties.GetWritingMode(note)); Assert.Same(expression, BindingOperations.GetBindingExpression(note, CssWritingModeProperties.WritingModeProperty));
    }

    public static IEnumerable<object[]> PhysicalCases()
    {
        foreach (var direction in new[] { "ltr", "rtl" })
        foreach (var align in new[] { "start", "center", "space-between", "space-around" }) yield return [direction, align];
    }

    [Theory, MemberData(nameof(PhysicalCases))]
    public void HorizontalAnnotationsUseVerticalGlyphGeometryOnThePhysicalRight(string direction, string align)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (ruby, basis, note) = Pair("horizontal-tb", "inter-character", direction, align); Layout(ruby, 220, 180);
        var a = Bounds(ruby, basis, 0, 1); var b = Bounds(ruby, note, 0, 2);
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(note));
        Assert.True(b.Left >= a.Right - .01, $"base={a}; note={b}");
        Assert.True(note.RenderSize.Height >= basis.RenderSize.Height - .01, $"base size={basis.RenderSize}; note size={note.RenderSize}");
        var one = Bounds(ruby, note, 0, 1); var two = Bounds(ruby, note, 1, 1);
        Assert.True(two.Top > one.Top, $"first={one}; second={two}");
        Assert.InRange(Math.Abs(one.Left - two.Left), 0, .01);
        for (int i = 0; i < 2; i++)
        {
            var local = Assert.Single(note.DocumentTextBounds(i, 1));
            Assert.Contains(note.DocumentCharacterFromPoint(new(local.Left + local.Width / 2, local.Top + local.Height / 2)), new[] { i, i + 1 });
        }
        note.SelectAll(); basis.SelectAll(); Assert.Equal("ㄓㄨ", note.SelectedText); Assert.Equal("字", basis.SelectedText);
        Assert.Same(ruby, note.VisualParent); Assert.Equal(2, ruby.Children.Count);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void VerticalContainersUseTheSameGeometryAsOver(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a = Pair(mode, "inter-character", "ltr", "center"); var b = Pair(mode, "over", "ltr", "center");
        Layout(a.Root, 220, 220); Layout(b.Root, 220, 220);
        Assert.Equal(CssWritingModeProperties.GetWritingMode(a.Root), CssWritingModeProperties.GetWritingMode(a.Note));
        Assert.Equal(Bounds(b.Root, b.Base, 0, 1), Bounds(a.Root, a.Base, 0, 1));
        Assert.Equal(Bounds(b.Root, b.Note, 0, 2), Bounds(a.Root, a.Note, 0, 2));
    }

    [Fact]
    public void ABaseAndItsRightHandNoteStayAssociatedWhenSharingAndBreakingAParagraph()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var paragraph = new StackPanel(); var before = Text("先", 32); Css.SetStyle(before, "display:inline;"); paragraph.Children.Add(before);
        var ruby = new StackPanel(); paragraph.Children.Add(ruby); var bases = new List<TextBlock>(); var notes = new List<TextBlock>();
        for (int i = 0; i < 3; i++)
        {
            var a = Text("字", 32); var b = Text("ㄓㄨ", 16); bases.Add(a); notes.Add(b); ruby.Children.Add(a); ruby.Children.Add(b);
            Css.SetStyle(a, "display:ruby-base;font-size:32px;"); Css.SetStyle(b, "display:ruby-text;font-size:16px;text-orientation:upright;");
        }
        Css.SetStyle(paragraph, "display:block;white-space:pre-wrap;font-family:'PingFang TC';font-size:32px;line-height:1.25;");
        Css.SetStyle(ruby, "display:ruby;ruby-position:inter-character;ruby-align:center;"); Layout(paragraph, 96, 220);
        var first = Bounds(paragraph, bases[0], 0, 1); var last = Bounds(paragraph, bases[2], 0, 1);
        Assert.True(first.Left >= 31.9 && last.Top > first.Top, $"first={first}; last={last}");
        for (int i = 0; i < 3; i++)
        {
            var a = Bounds(paragraph, bases[i], 0, 1); var b = Bounds(paragraph, notes[i], 0, 2);
            Assert.True(b.Left >= a.Right - .01 && Math.Abs(b.Top + b.Height / 2 - a.Top - a.Height / 2) < .1, $"base={a}; note={b}");
        }
    }

    [Fact]
    public void ComputedModeFollowsExplicitInheritanceRoleChangesAndReparenting()
    {
        var first = new StackPanel(); var second = new StackPanel(); var note = new StackPanel(); var child = Text("注", 16);
        note.Children.Add(child); first.Children.Add(note);
        Css.SetStyle(first, "display:ruby;ruby-position:inter-character;"); Css.SetStyle(second, "display:ruby;ruby-position:over;");
        Css.SetStyle(note, "display:ruby-text;writing-mode:vertical-lr;"); Css.SetStyle(child, "display:inline;writing-mode:inherit;"); Flush();
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(child));
        Css.SetStyle(note, "display:inline;writing-mode:vertical-lr;"); Flush();
        Assert.Equal(CssWritingMode.VerticalLr, CssWritingModeProperties.GetWritingMode(child));
        Css.SetStyle(note, "display:ruby-text;writing-mode:vertical-lr;"); Flush();
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(child));
        first.Children.Remove(note); second.Children.Add(note); Flush();
        Assert.Equal(CssWritingMode.VerticalLr, CssWritingModeProperties.GetWritingMode(child));
        second.Children.Remove(note); first.Children.Add(note); Flush();
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(child));
        Css.SetStyle(child, "display:inline;writing-mode:initial;"); Flush();
        Assert.Equal(CssWritingMode.HorizontalTb, CssWritingModeProperties.GetWritingMode(child));
        Css.SetStyle(child, "display:inline;writing-mode:inherit;"); Flush();
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(child));
        Css.SetStyle(first, "display:ruby;ruby-position:inter-character;writing-mode:vertical-lr;"); Flush();
        Assert.Equal(CssWritingMode.VerticalLr, CssWritingModeProperties.GetWritingMode(child));
    }

    public static IEnumerable<object[]> ContentCases()
    {
        foreach (var value in new[] { "ㄓ", "ㄓㄨ", "ㄓㄨㄥˋ", "Ab", "注音" })
        foreach (var align in new[] { "start", "center", "space-between", "space-around" }) yield return [value, align];
    }

    [Theory, MemberData(nameof(ContentCases))]
    public void AutomaticHeightAndGlyphDistributionRespectRubyAlignment(string value, string align)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (ruby, basis, note) = Pair("horizontal-tb", "inter-character", "ltr", align); note.Text = value;
        // A local text-align must survive; ruby-align still owns annotation distribution.
        note.TextAlignment = TextAlignment.Right; Layout(ruby, 240, 240);
        var a = ElementBounds(ruby, basis); var b = ElementBounds(ruby, note); var glyph = Bounds(ruby, note, 0, value.Length);
        Assert.True(b.Left >= a.Right - .01 && b.Height >= a.Height - .01, $"base={a}; note={b}");
        if (align == "start") Assert.InRange(Math.Abs(b.Top - a.Top), 0, .01);
        else Assert.InRange(Math.Abs(b.Top + b.Height / 2 - a.Top - a.Height / 2), 0, .01);
        if (align == "start") Assert.InRange(Math.Abs(glyph.Top - b.Top), 0, .01);
        else if (value != "注音" || align == "center")
            Assert.InRange(Math.Abs(glyph.Top + glyph.Height / 2 - b.Top - b.Height / 2), 0, .1);
        Assert.Equal(TextAlignment.Right, note.TextAlignment);
        note.SelectAll(); Assert.Equal(value, note.SelectedText);
        if (value is "ㄓㄨ" or "Ab")
        {
            var first = Bounds(ruby, note, 0, 1); var last = Bounds(ruby, note, 1, 1);
            Assert.InRange(last.Top - first.Bottom, -.01, .01);
        }
    }

    [Theory]
    [InlineData("start")]
    [InlineData("center")]
    public void ExplicitShortHeightIsNotExpanded(string align)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (ruby, basis, note) = Pair("horizontal-tb", "inter-character", "ltr", align);
        note.Text = "ㄓ"; note.Height = 24; Layout(ruby, 220, 180);
        var a = ElementBounds(ruby, basis); var b = ElementBounds(ruby, note);
        Assert.Equal(24, b.Height, 3);
        Assert.Equal(align == "start" ? a.Top : a.Top + (a.Height - 24) / 2, b.Top, 3);
    }

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void SideLevelsStackRightwardsAndContainersEncloseTheWholeBaseLevel(string direction)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var rbc = new StackPanel(); var basis = Text("字", 32); rbc.Children.Add(basis); root.Children.Add(rbc);
        Css.SetStyle(rbc, "display:ruby-base-container;"); Css.SetStyle(basis, "display:ruby-base;");
        var rows = new List<StackPanel>(); var notes = new List<TextBlock>();
        foreach (var text in new[] { "ㄓㄨ", "ㄓㄨㄥˋ" })
        {
            var rtc = new StackPanel(); var note = Text(text, 16); rows.Add(rtc); notes.Add(note); rtc.Children.Add(note); root.Children.Add(rtc);
            Css.SetStyle(rtc, "display:ruby-text-container;ruby-position:inter-character;");
            Css.SetStyle(note, "display:ruby-text;ruby-position:under;text-orientation:upright;");
        }
        Css.SetStyle(root, $"display:ruby;ruby-position:over;direction:{direction};font-family:'PingFang TC';font-size:32px;line-height:1.25;white-space:pre;");
        Layout(root, 240, 240);
        var a = ElementBounds(root, basis); var b = ElementBounds(root, notes[0]); var c = ElementBounds(root, notes[1]);
        Assert.Equal(a.Right, b.Left, 3); Assert.Equal(b.Right, c.Left, 3);
        var container = ElementBounds(root, rbc); Assert.Equal(a.Left, container.Left, 3); Assert.Equal(c.Right, container.Right, 3);
        Assert.All(rows, row => Assert.Equal(container, ElementBounds(root, row)));
        Assert.All(notes, note => Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(note)));
        Assert.All(rows, row => Assert.Equal(CssWritingMode.HorizontalTb, CssWritingModeProperties.GetWritingMode(row)));
        Assert.Same(rbc, basis.VisualParent); Assert.All(notes, note => Assert.Contains(note.VisualParent, rows));
    }

    [Fact]
    public void RepeatedPositionWritingModeContentAndAlignmentChangesRestoreSourceGeometry()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, basis, note) = Pair("horizontal-tb", "inter-character", "rtl", "space-around"); Layout(root, 220, 220);
        var originalBase = Bounds(root, basis, 0, 1); var originalNote = Bounds(root, note, 0, 2);
        for (int repeat = 0; repeat < 3; repeat++)
        {
            foreach (var mode in new[] { "horizontal-tb", "vertical-lr", "horizontal-tb" })
            foreach (var position in new[] { "over", "inter-character", "under", "inter-character" })
            {
                Css.SetStyle(root, $"display:ruby;writing-mode:{mode};ruby-position:{position};ruby-align:start;direction:rtl;line-height:1.25;white-space:pre;");
                note.Text = repeat == 1 ? "" : "ㄓㄨㄥˋ"; Layout(root, 96, 220);
                Assert.Equal(position == "inter-character" && mode == "horizontal-tb" ? CssWritingMode.VerticalRl : Enum.Parse<CssWritingMode>(mode.Replace("-", ""), true), CssWritingModeProperties.GetWritingMode(note));
                if (note.Text.Length == 0)
                {
                    // This API clamps the requested range to an empty caret;
                    // empty text may retain its insertion-position rectangle.
                    Assert.All(note.DocumentTextBounds(0, 1), rect => Assert.True(rect.Width <= 1.01 || rect.Height <= 1.01));
                    note.SelectAll(); Assert.Equal("", note.SelectedText);
                }
            }
        }
        note.Text = "ㄓㄨ"; Css.SetStyle(root, "display:ruby;writing-mode:horizontal-tb;ruby-position:inter-character;ruby-align:space-around;direction:rtl;font-family:'PingFang TC';font-size:32px;line-height:1.25;white-space:pre;"); Layout(root, 220, 220);
        Assert.Equal(originalBase, Bounds(root, basis, 0, 1)); Assert.Equal(originalNote, Bounds(root, note, 0, 2)); Assert.Equal(2, root.Children.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousAnnotationsKeepDomInheritanceAndCenterTheirOrthogonalContents(bool multiple)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var basis = Text("字", 32); var rtc = new StackPanel(); root.Children.Add(basis); root.Children.Add(rtc);
        Css.SetStyle(basis, "display:ruby-base;"); Css.SetStyle(rtc, "display:ruby-text-container;");
        var notes = new List<TextBlock>();
        foreach (var value in multiple ? new[] { "ㄓ", "ㄨ" } : new[] { "ㄓ" })
        { var text = Text(value, 16); notes.Add(text); rtc.Children.Add(text); Css.SetStyle(text, "display:inline;"); }
        Css.SetStyle(root, "display:ruby;ruby-position:inter-character;ruby-align:center;font-family:'PingFang TC';font-size:32px;line-height:1.25;white-space:pre;"); Layout(root, 220, 220);
        var a = ElementBounds(root, basis); var glyph = Rect.Empty;
        foreach (var text in notes)
        {
            Assert.Equal(CssWritingMode.HorizontalTb, CssWritingModeProperties.GetWritingMode(text));
            var b = Bounds(root, text, 0, 1); Assert.True(b.Left >= a.Right - .01, $"base={a}; note={b}"); glyph.Union(b);
            Assert.Same(rtc, text.VisualParent);
        }
        Assert.True(Math.Abs(glyph.Top + glyph.Height / 2 - a.Top - a.Height / 2) < .01,
            $"base={a}; glyph={glyph}; sources=" + string.Join(";", notes.Select(text => $"{ElementBounds(root,text)} desired={text.DesiredSize} native={text.UsesNativeRubyContent}")));
        var ordinary = new StackPanel(); var fresh = new StackPanel();
        Css.SetStyle(ordinary, "display:block;line-height:1.25;white-space:pre;"); Css.SetStyle(fresh, "display:block;line-height:1.25;white-space:pre;");
        foreach (var text in notes)
        {
            rtc.Children.Remove(text); ordinary.Children.Add(text);
            var copy = Text(text.Text, 16); fresh.Children.Add(copy); Css.SetStyle(copy, "display:inline;");
        }
        Layout(ordinary, 220, 220); Layout(fresh, 220, 220);
        Assert.Equal(Bounds(fresh, (TextBlock)fresh.Children[0], 0, 1), Bounds(ordinary, notes[0], 0, 1));
    }

    [Theory]
    [InlineData("center")]
    [InlineData("space-between")]
    [InlineData("space-around")]
    public void AnExplicitAnnotationPanelRetainsOriginalVerticalSourcesWithoutDefaultBopomofoGaps(string align)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var basis = Text("字", 32); var note = new StackPanel(); var first = Text("ㄓ", 16); var last = Text("ㄨ", 16);
        root.Children.Add(basis); root.Children.Add(note); note.Children.Add(first); note.Children.Add(last);
        Css.SetStyle(basis, "display:ruby-base;"); Css.SetStyle(note, "display:ruby-text;");
        Css.SetStyle(first, "display:inline;text-orientation:upright;"); Css.SetStyle(last, "display:inline;text-orientation:upright;");
        Css.SetStyle(root, $"display:ruby;ruby-position:inter-character;ruby-align:{align};font-family:'PingFang TC';font-size:32px;line-height:1.25;white-space:pre;"); Layout(root, 220, 220);
        var a = ElementBounds(root, basis); var b = Bounds(root, first, 0, 1); var c = Bounds(root, last, 0, 1);
        Assert.True(b.Left >= a.Right - .01, $"base={a}; first={b}"); Assert.Equal(b.Bottom, c.Top, 3);
        Assert.Equal(a.Top + a.Height / 2, (b.Top + c.Bottom) / 2, 3);
        Assert.Equal(CssWritingMode.VerticalRl, CssWritingModeProperties.GetWritingMode(first)); Assert.Same(note, first.VisualParent);
    }

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AnInterlinearSpanMeasuresTheSideColumnBetweenBasesExactlyOnce(string direction)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var rbc = new StackPanel(); root.Children.Add(rbc); Css.SetStyle(rbc, "display:ruby-base-container;");
        var bases = new List<TextBlock>();
        foreach (var value in new[] { "中", "文" })
        { var text = Text(value, 32); bases.Add(text); rbc.Children.Add(text); Css.SetStyle(text, "display:ruby-base;"); }
        var side = new StackPanel(); root.Children.Add(side); Css.SetStyle(side, "display:ruby-text-container;ruby-position:inter-character;");
        var sideNotes = new List<TextBlock>();
        foreach (var value in new[] { "ㄓㄨ", "ㄨㄣ" })
        { var text = Text(value, 16); sideNotes.Add(text); side.Children.Add(text); Css.SetStyle(text, "display:ruby-text;text-orientation:upright;"); }
        var over = new StackPanel(); root.Children.Add(over); var span = Text("跨越兩字注音", 16); over.Children.Add(span);
        Css.SetStyle(over, "display:ruby-text-container;ruby-position:over;"); Css.SetStyle(span, "display:ruby-text;");
        Css.SetStyle(root, $"display:ruby;ruby-align:center;direction:{direction};line-height:1.25;white-space:pre;"); Layout(root, 240, 220);
        var a = ElementBounds(root, bases[0]); var b = ElementBounds(root, bases[1]); var note = ElementBounds(root, span);
        // Each 38px column centers its original 32px source; the 20px side
        // column consumes the intervening span before distributing 12px slack.
        Assert.Equal(32, a.Width, 3); Assert.Equal(32, b.Width, 3);
        Assert.Equal(26, b.Left - a.Right, 3); Assert.Equal(96, note.Width, 3);
        Assert.Equal(a.Left - 3, note.Left, 3); Assert.Equal(b.Right + 3, note.Right, 3); Assert.True(note.Bottom <= a.Top + .01);
        Assert.Equal(116, ElementBounds(root, rbc).Width, 3); Assert.Equal(ElementBounds(root, rbc), ElementBounds(root, side));
        for (int i = 0; i < 2; i++) Assert.Equal(ElementBounds(root, bases[i]).Right + 3, ElementBounds(root, sideNotes[i]).Left, 3);
    }

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ASpanningSideNoteFollowsTheRightmostBaseAndKeepsTheWholeSpanOnOneLine(string direction)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var before = Text("先", 32); var ruby = new StackPanel(); root.Children.Add(before); root.Children.Add(ruby);
        Css.SetStyle(before, "display:inline;"); Css.SetStyle(ruby, "display:ruby;ruby-position:inter-character;ruby-align:center;");
        var bases = new List<TextBlock>();
        foreach (var value in new[] { "中", "文" })
        { var text = Text(value, 32); bases.Add(text); ruby.Children.Add(text); Css.SetStyle(text, "display:ruby-base;"); }
        var note = Text("ㄓㄨ", 16); ruby.Children.Add(note); Css.SetStyle(note, "display:ruby-text;text-orientation:upright;");
        Css.SetStyle(root, $"display:block;direction:{direction};font-size:32px;line-height:1.25;white-space:pre-wrap;"); Layout(root, 96, 220);
        var a = ElementBounds(root, bases[0]); var b = ElementBounds(root, bases[1]); var c = ElementBounds(root, note);
        Assert.Equal(a.Top, b.Top, 3); Assert.Equal(Math.Max(a.Right, b.Right), c.Left, 3);
        Assert.True(a.Top > ElementBounds(root, before).Top, $"before={ElementBounds(root, before)}; base={a}");
        var font = TextMeasurement.GetFontMetrics("PingFang TC", 32, 400, 0);
        Assert.Equal(font.Ascent + font.Descent, c.Height, 3); Assert.Equal(3, ruby.Children.Count);
    }

    [Fact]
    public void AllocatedSideContainersDoNotRelayoutTheirChildrenDuringAnOuterResize()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var outer = new Grid(); var ruby = new StackPanel { Width = 220, Height = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        outer.Children.Add(ruby); var basis = Text("漢字", 32); ruby.Children.Add(basis); Css.SetStyle(basis, "display:ruby-base;");
        var notes = new List<TextBlock>();
        foreach (var value in new[] { "かんじ", "汉字", "Kanji" })
        {
            var row = new StackPanel(); ruby.Children.Add(row); var note = Text(value, 16); notes.Add(note); row.Children.Add(note);
            Css.SetStyle(row, "display:ruby-text-container;"); Css.SetStyle(note, "display:ruby-text;text-orientation:upright;");
        }
        Set("horizontal-tb"); Layout(outer, 1040, 700); var original = notes.Select(note => Bounds(ruby, note, 0, note.Text.Length)).ToArray();
        foreach (var mode in new[] { "vertical-rl", "vertical-lr", "horizontal-tb" }) { Set(mode); Layout(outer, 1040, 700); }
        ruby.Width = 164; ruby.Height = 230; Layout(outer, 860, 630);
        // The native window drains descendant requests after its parent pass.
        // Exercise that queue explicitly; a headless root-only Measure/Arrange
        // loop does not revisit these structural RTC containers independently.
        var manager = new LayoutManager();
        foreach (var row in ruby.Children.OfType<StackPanel>())
        { row.InvalidateMeasure(); manager.InvalidateMeasure(row); }
        manager.UpdateLayout(outer, new(860,630));
        var previous = ElementBounds(ruby, basis);
        foreach (var note in notes)
        {
            var rect = ElementBounds(ruby, note); Assert.True(rect.Left >= previous.Right - .01, $"previous={previous}; note={rect}"); previous = rect;
        }
        ruby.Width = 220; ruby.Height = 300; Layout(outer, 1040, 700);
        Assert.Equal(original, notes.Select(note => Bounds(ruby, note, 0, note.Text.Length)).ToArray());
        void Set(string mode) => Css.SetStyle(ruby, $"display:ruby;writing-mode:{mode};ruby-position:inter-character;ruby-align:center;font-family:'PingFang TC';font-size:32px;line-height:1.25;white-space:pre-wrap;");
    }

    [Theory]
    [InlineData("auto", "space-between", 40)]
    [InlineData("inter-character", "space-between", 40)]
    [InlineData("none", "space-between", 32)]
    [InlineData("auto", "space-around", 36)]
    [InlineData("inter-character", "space-around", 36)]
    [InlineData("none", "space-around", 32)]
    public void ExplicitJustificationOverridesTheDefaultBopomofoPolicyAndCanBeRemoved(string justify, string align, double height)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, basis, note) = Pair("horizontal-tb", "inter-character", "ltr", align);
        // Explicit side-note height preserves a real 8px distribution region;
        // the base's 40px line-height is not its 32px font content area.
        Css.SetStyle(note, "display:ruby-text;font-size:16px;text-orientation:upright;height:40px;"); Layout(root, 220, 220);
        var original = Bounds(root, note, 0, 2); Assert.Equal(32, original.Height, 3);
        Css.SetStyle(note, $"display:ruby-text;font-size:16px;text-orientation:upright;height:40px;text-justify:{justify};"); Layout(root, 220, 220);
        Assert.Equal(height, Bounds(root, note, 0, 2).Height, 3);
        Css.SetStyle(note, "display:ruby-text;font-size:16px;text-orientation:upright;height:40px;"); Layout(root, 220, 220);
        Assert.Equal(original, Bounds(root, note, 0, 2)); Assert.Same(root, basis.VisualParent);
    }

    private static Rect ElementBounds(FrameworkElement host, FrameworkElement element)
    {
        Assert.True(host.GetRenderMatrix().TryInvert(out var inverse));
        var point = Matrix.Multiply(element.GetRenderMatrix(), inverse).Transform(new Point(0,0));
        return new(point.X, point.Y, element.RenderSize.Width, element.RenderSize.Height);
    }

    private sealed class ModeSource : INotifyPropertyChanged
    {
        private CssWritingMode _mode;
        public CssWritingMode Mode { get => _mode; set { _mode = value; PropertyChanged?.Invoke(this, new(nameof(Mode))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static (StackPanel Root, TextBlock Base, TextBlock Note) Pair(string mode, string position, string direction, string align)
    {
        var ruby = new StackPanel(); var basis = Text("字", 32); var note = Text("ㄓㄨ", 16); ruby.Children.Add(basis); ruby.Children.Add(note);
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;text-orientation:upright;");
        Css.SetStyle(ruby, $"display:ruby;writing-mode:{mode};ruby-position:{position};ruby-align:{align};direction:{direction};font-family:'PingFang TC';font-size:32px;line-height:1.25;white-space:pre;");
        return (ruby, basis, note);
    }
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, FontFamily = new FontFamily("PingFang TC"), IsTextSelectionEnabled = true };
    private static Rect Bounds(FrameworkElement host, TextBlock text, int first, int length)
    {
        var local = Assert.Single(text.DocumentTextBounds(first, length)); Assert.True(host.GetRenderMatrix().TryInvert(out var inverse));
        var matrix = Matrix.Multiply(text.GetRenderMatrix(), inverse); var a = matrix.Transform(local.TopLeft); var b = matrix.Transform(local.BottomRight);
        return new(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y));
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Layout(FrameworkElement root, double width, double height)
    { for (int i = 0; i < 3; i++) { Flush(); root.Measure(new(width,height)); root.Arrange(new(0,0,width,height)); root.UpdateLayout(); } }
}
