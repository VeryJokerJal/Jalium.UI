using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssRubyRuntimeTests
{
    [Theory]
    [InlineData("ruby", "Ruby")]
    [InlineData("ruby-base", "RubyBase")]
    [InlineData("ruby-text", "RubyText")]
    [InlineData("ruby-base-container", "RubyBaseContainer")]
    [InlineData("ruby-text-container", "RubyTextContainer")]
    public void DisplayRolesReachTheActualLayoutElement(string value, string expected)
    {
        var parent = new StackPanel(); var child = new TextBlock { Text = "字" }; parent.Children.Add(child);
        Css.SetStyle(child, "display:" + value); Flush();
        Assert.Equal(expected, child.CssDisplayMode.ToString());
    }

    [Theory]
    [InlineData("horizontal-tb", "over")]
    [InlineData("horizontal-tb", "under")]
    [InlineData("vertical-rl", "over")]
    [InlineData("vertical-rl", "under")]
    [InlineData("vertical-lr", "over")]
    [InlineData("vertical-lr", "under")]
    public void OriginalBaseAndAnnotationHaveRealSourceBoundsAndCorrectSides(string mode, string position)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var ruby = new StackPanel(); var basis = Text("漢", 32); var note = Text("かん", 16);
        ruby.Children.Add(basis); ruby.Children.Add(note);
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
        Css.SetStyle(ruby, $"display:ruby;writing-mode:{mode};ruby-position:{position};ruby-align:center;font-family:'Hiragino Sans';font-size:32px;white-space:pre;line-height:1.25;");
        Layout(ruby, 220, 220);
        var a = HostBounds(ruby, basis, 0, 1); var b = HostBounds(ruby, note, 0, 2);
        Assert.True(a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0);
        if (mode == "horizontal-tb") Assert.True(position == "over" ? b.Bottom <= a.Top + .01 : b.Top >= a.Bottom - .01, $"base={a}; annotation={b}");
        else Assert.True(position == "over" ? b.Left >= a.Right - .01 : b.Right <= a.Left + .01, $"base={a}; annotation={b}");
        Assert.Same(ruby, basis.VisualParent); Assert.Same(ruby, note.VisualParent);
        basis.SelectAll(); note.SelectAll(); Assert.Equal("漢", basis.SelectedText); Assert.Equal("かん", note.SelectedText);
        var local = Assert.Single(note.DocumentTextBounds(0, 1));
        Assert.Contains(note.DocumentCharacterFromPoint(new Point(local.Left + local.Width / 2, local.Top + local.Height / 2)), new[] { 0, 1 });
    }

    [Fact]
    public void IndependentPairsBreakAcrossLinesInsteadOfMovingAsOneAtomicRuby()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var paragraph = new StackPanel(); var before = Text("先", 32); Css.SetStyle(before, "display:inline;");
        var ruby = new StackPanel(); var bases = new List<TextBlock>(); var notes = new List<TextBlock>();
        foreach (var pair in new[] { ("東", "とう"), ("京", "きょう"), ("都", "と") })
        {
            var basis = Text(pair.Item1, 32); var note = Text(pair.Item2, 16); bases.Add(basis); notes.Add(note);
            Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
            ruby.Children.Add(basis); ruby.Children.Add(note);
        }
        paragraph.Children.Add(before); paragraph.Children.Add(ruby);
        Css.SetStyle(ruby, "display:ruby;ruby-align:center;");
        Css.SetStyle(paragraph, "display:block;font-family:'Hiragino Sans';font-size:32px;line-height:1.25;white-space:pre-wrap;");
        Layout(paragraph, 96, 240);
        var first = HostBounds(paragraph, bases[0], 0, 1); var last = HostBounds(paragraph, bases[^1], 0, 1);
        Assert.True(first.Left >= 31.9, $"first={first}; before={HostBounds(paragraph, before, 0, 1)}");
        Assert.True(last.Top > first.Top, $"first={first}; last={last}");
        for (int i = 0; i < bases.Count; i++)
        {
            var basis = HostBounds(paragraph, bases[i], 0, 1);
            var note = HostBounds(paragraph, notes[i], 0, notes[i].Text.Length);
            Assert.True(note.Bottom <= basis.Top + .01, $"pair {i}: base={basis}; annotation={note}");
            Assert.InRange(Math.Abs(note.Left + note.Width / 2 - basis.Left - basis.Width / 2), 0, .01);
        }
        Assert.Same(ruby, paragraph.Children[1]); Assert.All(bases, text => Assert.Same(ruby, text.VisualParent));
    }

    public static IEnumerable<object[]> MultiLevelCases()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr" })
        foreach (var position in new[] { "", "alternate under", "over", "under" })
            yield return [mode, position];
    }

    [Theory, MemberData(nameof(MultiLevelCases))]
    public void MultipleLevelsHonorDefaultAlternationAndStackOutward(string mode, string position)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var ruby = new StackPanel(); var basis = Text("漢", 32); ruby.Children.Add(basis);
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;");
        var notes = new List<TextBlock>();
        for (int i = 0; i < 3; i++)
        {
            var row = new StackPanel(); var note = Text(new[] { "かん", "漢字", "Kan" }[i], 16); notes.Add(note);
            Css.SetStyle(row, "display:ruby-text-container;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
            row.Children.Add(note); ruby.Children.Add(row);
        }
        Css.SetStyle(ruby, $"display:ruby;writing-mode:{mode};ruby-align:center;font-family:'Hiragino Sans';font-size:32px;white-space:pre;line-height:1.25;{(position.Length == 0 ? "" : "ruby-position:" + position)}");
        Layout(ruby, 300, 300);
        var a = HostBounds(ruby, basis, 0, 1);
        var bounds = notes.Select(note => HostBounds(ruby, note, 0, note.Text.Length)).ToArray();
        for (int i = 0; i < 3; i++)
        {
            bool under = position == "under" || position == "alternate under" && i % 2 == 0 || position.Length == 0 && i % 2 == 1;
            var b = bounds[i];
            Assert.True(mode == "horizontal-tb" ? under ? b.Top >= a.Bottom - .01 : b.Bottom <= a.Top + .01
                : under ? b.Right <= a.Left + .01 : b.Left >= a.Right - .01, $"level {i}: base={a}; annotation={b}");
        }
        Assert.True(mode == "horizontal-tb" ? Math.Min(bounds[0].Bottom, bounds[2].Bottom) - Math.Max(bounds[0].Top, bounds[2].Top) <= .01
            : Math.Min(bounds[0].Right, bounds[2].Right) - Math.Max(bounds[0].Left, bounds[2].Left) <= .01, $"levels overlap: {bounds[0]} / {bounds[2]}");
        Assert.All(notes, note => { note.SelectAll(); Assert.Equal(note.Text, note.SelectedText); });
    }

    public static IEnumerable<object[]> DistributionCases()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr" })
        foreach (var align in new[] { "start", "center", "space-between", "space-around" })
        foreach (var direction in new[] { "ltr", "rtl" }) yield return [mode, align, direction];
    }

    [Theory, MemberData(nameof(DistributionCases))]
    public void RealTextDistributionUsesTheSameNativeBoundsAndHitGeometry(string mode, string align, string direction)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var ruby = new StackPanel(); var basis = Text("東京都", 32); var note = Text("とう", 16);
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
        ruby.Children.Add(basis); ruby.Children.Add(note);
        Css.SetStyle(ruby, $"display:ruby;writing-mode:{mode};direction:{direction};ruby-position:over;ruby-align:{align};font-family:'Hiragino Sans';font-size:32px;white-space:pre;line-height:1.25;text-autospace:no-autospace;");
        Layout(ruby, 240, 240);
        var a = HostBounds(ruby, basis, 0, 3); var b = HostBounds(ruby, note, 0, 2);
        double baseStart = mode == "horizontal-tb" ? a.Left : a.Top, baseLength = mode == "horizontal-tb" ? a.Width : a.Height;
        double noteStart = mode == "horizontal-tb" ? b.Left : b.Top, noteLength = mode == "horizontal-tb" ? b.Width : b.Height;
        if (align is "center" or "space-around") Assert.InRange(Math.Abs(noteStart + noteLength / 2 - baseStart - baseLength / 2), 0, .1);
        if (align == "space-between") Assert.InRange(noteLength, baseLength - .1, baseLength + .1);
        if (align == "space-around") Assert.True(noteLength > 40 && noteLength < baseLength - .1, $"base={a}; annotation={b}");
        if (align == "start") Assert.InRange(Math.Abs(direction == "ltr" ? noteStart - baseStart : noteStart + noteLength - baseStart - baseLength), 0, .1);
        for (int i = 0; i < 2; i++)
        {
            var local = Assert.Single(note.DocumentTextBounds(i, 1));
            Assert.Contains(note.DocumentCharacterFromPoint(new(local.Left + local.Width / 2, local.Top + local.Height / 2)), new[] { i, i + 1 });
        }
        note.Select(1, 1); Assert.Equal("う", note.SelectedText);
        Assert.Equal(TextAlignment.Left, note.TextAlignment);
    }

    [Fact]
    public void MissingRubyContainerPairsInternalSiblingsWithoutReparenting()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var host = new StackPanel(); var basis = Text("字", 32); var note = Text("じ", 16);
        host.Children.Add(basis); host.Children.Add(note);
        Css.SetStyle(host, "display:block;font-family:'Hiragino Sans';font-size:32px;white-space:pre;line-height:1.25;");
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
        Layout(host, 180, 180);
        var a = HostBounds(host, basis, 0, 1); var b = HostBounds(host, note, 0, 1);
        Assert.True(b.Bottom <= a.Top + .01, $"base={a}; annotation={b}");
        Assert.Same(host, basis.VisualParent); Assert.Same(host, note.VisualParent);
    }

    [Fact]
    public void RestyleAndRepeatedMeasureReleaseAnonymousCellsAndKeepOriginalNodes()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var host = new StackPanel(); var ruby = new StackPanel(); var basis = Text("漢", 32); var note = Text("かん", 16);
        host.Children.Add(ruby); ruby.Children.Add(basis); ruby.Children.Add(note);
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
        const string style = "display:ruby;ruby-align:space-between;font-family:'Hiragino Sans';font-size:32px;white-space:pre;line-height:1.25;";
        Css.SetStyle(ruby, style); Layout(host, 200, 180);
        var original = HostBounds(host, note, 0, 2);
        int count = ruby.VisualChildrenCount;
        for (int i = 0; i < 12; i++)
        {
            note.Text = i % 2 == 0 ? "かんかん" : "かん"; Layout(host, i % 2 == 0 ? 96 : 200, 180);
            Assert.Equal(count, ruby.VisualChildrenCount);
        }
        Assert.Equal(original, HostBounds(host, note, 0, 2));
        Css.SetStyle(ruby, "display:block;"); Layout(host, 200, 180);
        Assert.Equal(2, ruby.VisualChildrenCount);
        Css.SetStyle(ruby, style); Layout(host, 200, 180);
        Assert.Equal(count, ruby.VisualChildrenCount); Assert.Equal(original, HostBounds(host, note, 0, 2));
        Assert.Equal(2, ruby.Children.Count); Assert.Same(ruby, note.VisualParent); Assert.Same(basis, ruby.Children[0]);
    }

    [Fact]
    public void HiddenAndCollapsedAnnotationsPreservePairingWithDifferentAdvance()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var ruby = new StackPanel(); var a = Text("東", 32); var b = Text("京", 32); var first = Text("とうとう", 16); var last = Text("きょう", 16);
        foreach (var text in new[] { a, b, first, last }) ruby.Children.Add(text);
        Css.SetStyle(a, "display:ruby-base;font-size:32px;"); Css.SetStyle(b, "display:ruby-base;font-size:32px;");
        Css.SetStyle(last, "display:ruby-text;font-size:16px;");
        Css.SetStyle(ruby, "display:ruby;ruby-align:center;font-family:'Hiragino Sans';font-size:32px;white-space:pre;line-height:1.25;");
        Css.SetStyle(first, "display:ruby-text;font-size:16px;visibility:hidden;"); Layout(ruby, 240, 160);
        var hidden = HostBounds(ruby, b, 0, 1);
        Css.SetStyle(first, "display:ruby-text;font-size:16px;visibility:collapse;"); Layout(ruby, 240, 160);
        var collapsed = HostBounds(ruby, b, 0, 1); var annotation = HostBounds(ruby, last, 0, last.Text.Length);
        Assert.True(hidden.Left > collapsed.Left + 20, $"hidden={hidden}; collapsed={collapsed}");
        Assert.InRange(Math.Abs(annotation.Left + annotation.Width / 2 - collapsed.Left - collapsed.Width / 2), 0, .01);
        Assert.Same(ruby, first.VisualParent); Assert.Same(last, ruby.Children[3]);
    }

    [Fact]
    public void SharedParagraphResizeAndDisplayChangesDoNotAccumulateFormattingChildren()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var host = new StackPanel(); var prefix = Text("先", 32); var ruby = new StackPanel(); var basis = Text("東京都", 32); var note = Text("とう", 16);
        host.Children.Add(prefix); host.Children.Add(ruby); ruby.Children.Add(basis); ruby.Children.Add(note);
        Css.SetStyle(host, "display:block;font-family:'Hiragino Sans';font-size:32px;white-space:pre-wrap;line-height:1.25;"); Css.SetStyle(prefix, "display:inline;");
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
        Css.SetStyle(ruby, "display:ruby;ruby-align:space-between;"); Layout(host, 220, 200);
        var original = HostBounds(host, note, 0, 2); int count = ruby.VisualChildrenCount;
        for (int i = 0; i < 14; i++) { note.Text = i % 2 == 0 ? "とうきょうと" : "とう"; Layout(host, i % 2 == 0 ? 96 : 220, 200); Assert.Equal(count, ruby.VisualChildrenCount); }
        Assert.Equal(original, HostBounds(host, note, 0, 2));
        Css.SetStyle(ruby, "display:block;"); Layout(host, 220, 200); Assert.Equal(2, ruby.VisualChildrenCount);
        Css.SetStyle(ruby, "display:ruby;ruby-align:space-between;"); Layout(host, 220, 200); Assert.Equal(count, ruby.VisualChildrenCount);
        Assert.Equal(original, HostBounds(host, note, 0, 2)); Assert.Equal(2, ruby.Children.Count);
    }

    public static IEnumerable<object[]> EmptyEditCases()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr" })
        foreach (var target in new[] { "base", "annotation", "both" }) yield return [mode, target];
    }

    [Theory, MemberData(nameof(EmptyEditCases))]
    public void ClearingOriginalTextMatchesAFreshEmptyTreeAndCanBeRestored(string mode, string target)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var mutable = Create("漢", "かん"); Layout(mutable.Root, 220, 220);
        var original = mutable.Root.DesiredSize; int visualChildren = mutable.Root.VisualChildrenCount;
        for (int i = 0; i < 3; i++)
        {
            mutable.Base.Text = target is "base" or "both" ? "" : "漢";
            mutable.Note.Text = target is "annotation" or "both" ? "" : "かん";
            Layout(mutable.Root, 220, 220);
            var fresh = Create(mutable.Base.Text, mutable.Note.Text); Layout(fresh.Root, 220, 220);
            Assert.Equal(fresh.Root.DesiredSize, mutable.Root.DesiredSize);
            Assert.Equal(fresh.Base.RenderSize, mutable.Base.RenderSize); Assert.Equal(fresh.Note.RenderSize, mutable.Note.RenderSize);
            if (mutable.Base.Text.Length > 0) Assert.Equal(HostBounds(fresh.Root, fresh.Base, 0, 1), HostBounds(mutable.Root, mutable.Base, 0, 1));
            if (mutable.Note.Text.Length > 0) Assert.Equal(HostBounds(fresh.Root, fresh.Note, 0, 2), HostBounds(mutable.Root, mutable.Note, 0, 2));
            mutable.Base.Text = "漢"; mutable.Note.Text = "かん"; Layout(mutable.Root, 220, 220);
            Assert.Equal(original, mutable.Root.DesiredSize); Assert.Equal(visualChildren, mutable.Root.VisualChildrenCount);
        }
        Assert.Same(mutable.Root, mutable.Base.VisualParent); Assert.Same(mutable.Note, mutable.Root.Children[1]);
        (StackPanel Root, TextBlock Base, TextBlock Note) Create(string basis, string note)
        {
            var root = new StackPanel(); var a = Text(basis, 32); var b = Text(note, 16); root.Children.Add(a); root.Children.Add(b);
            Css.SetStyle(a, "display:ruby-base;font-size:32px;"); Css.SetStyle(b, "display:ruby-text;font-size:16px;");
            Css.SetStyle(root, $"display:ruby;writing-mode:{mode};ruby-align:space-between;font-family:'Hiragino Sans';font-size:32px;line-height:1.25;white-space:pre;");
            return (root, a, b);
        }
    }

    public static IEnumerable<object[]> PixelCases()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr" })
        foreach (var position in new[] { "over", "under" })
        foreach (var align in new[] { "start", "center", "space-between", "space-around" })
        foreach (var direction in new[] { "ltr", "rtl" })
        {
            yield return [mode, position, align, direction, RenderBackend.Software, RenderingEngine.Impeller];
            yield return [mode, position, align, direction, RenderBackend.Metal, RenderingEngine.Impeller];
            yield return [mode, position, align, direction, RenderBackend.Metal, RenderingEngine.Vello];
        }
    }

    [Theory, MemberData(nameof(PixelCases))]
    public void RubyPixelsMatchIndependentTextAtLiteralPhysicalSlots(string mode, string position, string align,
        string direction, RenderBackend backend, RenderingEngine engine)
    {
        if (backend == RenderBackend.Metal && Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") != "1")
        {
            var command = System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND")
                ?? throw new InvalidOperationException("Metal main-thread runner is required."))!;
            var start = new System.Diagnostics.ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in command.Skip(1).Concat(new[] { "ruby", mode, position, align, direction, engine.ToString() })) start.ArgumentList.Add(arg);
            start.Environment["JALIUM_CSS_METAL_CHILD"] = "1";
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(90000)) { process.Kill(entireProcessTree: true); Assert.Fail("Metal ruby runner exceeded 90 seconds."); }
            string receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, receipt); Assert.Contains("ENGINE " + engine, receipt); Assert.Contains("BACKEND Metal", receipt); return;
        }
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = backend == RenderBackend.Metal ? new NativePlatformWindow("Jalium CSS ruby pixels", 1500, 500, 320, 320, 1, 0) : null;
        surface?.Show(activate: false); Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        var ruby = new StackPanel(); var basis = Text("東京都", 32); var note = Text("とう", 16);
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;");
        ruby.Children.Add(basis); ruby.Children.Add(note);
        Css.SetStyle(ruby, $"display:ruby;writing-mode:{mode};direction:{direction};ruby-position:{position};ruby-align:{align};font-family:'Hiragino Sans';font-size:32px;white-space:pre;line-height:1.25;text-autospace:no-autospace;color:black;");
        Layout(ruby, 240, 240);

        // This reference has no ruby roles or shared ruby formatter. Its slots
        // are literal 32px/16px CJK advances. Font areas are independent of
        // the authored 40px line; the compact line advances by B + N.
        var canvas = new Canvas(); var referenceTexts = new List<TextBlock>();
        var baseFont = TextMeasurement.GetFontMetrics("Hiragino Sans", 32, 400, 0);
        var noteFont = TextMeasurement.GetFontMetrics("Hiragino Sans", 16, 400, 0);
        double B = (double)baseFont.Ascent + baseFont.Descent, N = (double)noteFont.Ascent + noteFont.Descent;
        double baseTop = position == "over" ? B / 2 + N - 20 : 20 - B / 2;
        double noteTop = position == "over" ? B / 2 - 20 : 20 + B / 2;
        double inlineStart = direction == "rtl" ? 144 : 0;
        double noteWidth = align == "space-between" ? 96 : align == "space-around" ? 64 : 32;
        double noteOffset = align == "start" ? direction == "rtl" ? 64 : 0 : (96 - noteWidth) / 2;
        Rect baseSlot, noteSlot;
        if (mode == "horizontal-tb")
        {
            baseSlot = new(inlineStart, baseTop, 96, B);
            noteSlot = new(inlineStart + noteOffset, noteTop, noteWidth, N);
        }
        else
        {
            baseSlot = new(mode == "vertical-rl" ? 240 - baseTop - B : B + N - baseTop - B, inlineStart, B, 96);
            noteSlot = new(mode == "vertical-rl" ? 240 - noteTop - N : B + N - noteTop - N, inlineStart + noteOffset, N, noteWidth);
        }
        Add("東京都", 32, baseSlot, false); Add("とう", 16, noteSlot, align is "space-between" or "space-around");
        Layout(canvas, 240, 240);
        var actual = Pixels(ruby.Render); var expected = Pixels(canvas.Render);
        Save("actual", actual); Save("reference", expected);
        var evidenceFolder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(evidenceFolder))
            File.WriteAllText(Path.Combine(evidenceFolder, $"ruby-{mode}-{position}-{align}-{direction}-{backend}-{engine}-geometry.json"),
                System.Text.Json.JsonSerializer.Serialize(new { basis = State(basis), note = State(note), reference = referenceTexts.Select(State), baseSlot, noteSlot }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        object State(TextBlock text)
        {
            var field = typeof(TextBlock).GetField("_layoutLines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            object? Private(object value, string name) => value.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(value);
            var bidi = Private(text, "_cssBidiParagraph"); var just = bidi is null ? null : Private(bidi, "Justification");
            return new { text.Text, text.RenderSize, rubyJustify = Private(text, "_cssRubyJustify"), intrinsic = Private(text, "_layoutIntrinsic"), dirty = Private(text, "_layoutDirty"), bidiJustification = just?.ToString(), matrix = text.GetRenderMatrix(), bounds = HostBounds(ReferenceEquals(text, basis) || ReferenceEquals(text, note) ? ruby : canvas, text, 0, text.Text.Length),
                lines = ((System.Collections.IEnumerable)field.GetValue(text)!).Cast<object>().Select(line => line.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    .Where(f => f.FieldType == typeof(double)).ToDictionary(f => f.Name, f => f.GetValue(line))) };
        }
        Assert.Contains(actual.Where((_, i) => i % 4 == 3), alpha => alpha > 0); Assert.Equal(expected, actual);

        void Add(string content, double size, Rect slot, bool justify)
        {
            var text = Text(content, size); text.Width = slot.Width; text.Height = slot.Height;
            var line = (size == 32 ? B : N).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            Css.SetStyle(text, $"font-family:'Hiragino Sans';font-size:{size}px;line-height:{line}px;white-space:pre;writing-mode:{mode};direction:{direction};text-autospace:no-autospace;color:black;" + (justify ? "text-align:justify;text-align-last:justify;" : "text-align:start;"));
            Canvas.SetLeft(text, slot.X); Canvas.SetTop(text, slot.Y); canvas.Children.Add(text); referenceTexts.Add(text);
        }
        byte[] Pixels(Action<DrawingContext> draw)
        {
            using var target = surface is null ? context.CreateOffscreenRenderTarget(320, 320) : context.CreateRenderTarget(surface.GetSurface(), 320, 320);
            target.SetRenderingEngine(engine); Assert.Equal(engine, target.RenderingEngine); Console.WriteLine("ENGINE " + target.RenderingEngine);
            target.SetDpi(96, 96); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
            using (var dc = new RenderTargetDrawingContext(target, context)) { dc.PushTransform(new TranslateTransform(32, 32)); draw(dc); dc.Pop(); dc.Close(); }
            target.EndDraw(); var pixels = new byte[320 * 320 * 4]; Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 320 * 4, out _, out _)); return pixels;
        }
        void Save(string kind, byte[] pixels)
        {
            var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder); var bitmap = BitmapSource.Create(320, 320, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, $"ruby-{mode}-{position}-{align}-{direction}-{backend}-{engine}-{kind}.png")); encoder.Save(stream);
        }
    }

    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size,
        FontFamily = new FontFamily("Hiragino Sans"), IsTextSelectionEnabled = true };
    private static Rect HostBounds(FrameworkElement host, TextBlock text, int start, int length)
    {
        var local = Assert.Single(text.DocumentTextBounds(start, length));
        Assert.True(host.GetRenderMatrix().TryInvert(out var inverse));
        var matrix = Matrix.Multiply(text.GetRenderMatrix(), inverse);
        var a = matrix.Transform(local.TopLeft); var b = matrix.Transform(local.BottomRight);
        return new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Layout(FrameworkElement element, double width, double height)
    {
        for (int i = 0; i < 3; i++) { Flush(); element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    }
}
