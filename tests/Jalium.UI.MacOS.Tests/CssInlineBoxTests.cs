using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class CssInlineBoxTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    private static Jalium.UI.Automation.Provider.ITextProvider ProviderFor(FlowDocumentPresentation body) => (Jalium.UI.Automation.Provider.ITextProvider)Jalium.UI.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(body)!.GetPattern(Jalium.UI.Automation.Peers.PatternInterface.Text)!;
    public static IEnumerable<object[]> Modes()
    {
        foreach (var document in new[]
        {
            false,
            true
        }
        )
            foreach (var target in new[]
            {
                "run",
                "span"
            }
            )
                foreach (var mode in new[]
                {
                    "inline",
                    "block",
                    "inline-block",
                    "flow-root",
                    "flex",
                    "inline-flex",
                    "grid",
                    "inline-grid",
                    "table",
                    "inline-table",
                    "table-row",
                    "table-cell",
                    "contents",
                    "none"
                }
                )
                    yield return[document, target, mode];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void OriginalInlineBoxesMatchOrdinaryCss(bool document, string target, string mode)
    {
        using var f = new Fixture(document, target);
        f.Style($"display:{mode};gap:6px;grid-template-columns:24px 36px;");
        f.Layout();
        f.SameGeometry();
        f.OriginalSource();
    }

    [Fact]
    public void ChangingParentDisplayRecomputesItsInlineChildren()
    {
        var parent = new StackPanel();
        var child = new TextBlock
        {
            Text = "AA"
        };
        parent.Children.Add(child);
        Css.SetStyle(child, "display:inline;");
        void Layout()
        {
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue();
                CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                parent.Measure(new(180, double.PositiveInfinity));
                parent.Arrange(new(0, 0, 180, parent.DesiredSize.Height));
            }
        }

        Css.SetStyle(parent, "display:block;");
        Layout();
        Assert.True(child.CssInlineOuter);
        Css.SetStyle(parent, "display:grid;");
        Layout();
        Assert.False(child.CssInlineOuter);
        Assert.Equal(CssDisplayMode.Block, child.CssDisplayMode);
        Css.SetStyle(parent, "display:block;");
        Layout();
        Assert.True(child.CssInlineOuter);
        Assert.Equal(CssDisplayMode.Native, child.CssDisplayMode);
    }

    public static IEnumerable<object[]> BoxStyles()
    {
        foreach (var document in new[]
        {
            false,
            true
        }
        )
            foreach (var target in new[]
            {
                "run",
                "span"
            }
            )
                foreach (var style in new[]
                {
                    "display:inline;padding:3px 5px;border:2px solid #146342;margin:0 4px;background:#eef5f0;border-radius:4px;",
                    "display:inline;box-decoration-break:clone;padding:2px 4px;border:1px solid green;",
                    "display:inline-block;width:50%;padding:3px;border:2px solid green;box-sizing:content-box;",
                    "display:block;width:50%;padding:3px;border:2px solid green;box-sizing:border-box;",
                    "display:grid;width:140px;height:64px;grid-template-columns:40% 60%;align-items:end;gap:3px;padding:4px;border:2px solid green;",
                    "display:flex;width:130px;gap:7px;justify-content:space-between;align-items:center;padding:4px;",
                    "display:table;width:120px;border-spacing:6px;border:1px solid green;",
                    "display:contents;width:120px;padding:10px;border:4px solid green;background:red;",
                    "display:inline;position:relative;left:7px;top:3px;",
                    "display:inline;font-size:21px;line-height:1.5;letter-spacing:1px;word-spacing:2px;",
                    "display:inline;vertical-align:super;line-height:18px;",
                    "display:inline;white-space:pre;"
                }
                )
                    yield return[document, target, style];
    }

    [Theory]
    [MemberData(nameof(BoxStyles))]
    public void InlineBoxStylesMatchOrdinaryCss(bool document, string target, string style)
    {
        using var f = new Fixture(document, target);
        f.Style(style);
        f.Layout();
        f.SameGeometry();
        f.OriginalSource();
        f.HitOriginalOffsets();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoxlessRunKeepsItsOriginalSharedLinePosition(bool document)
    {
        using var f = new Fixture(document, "run");
        f.Style("display:contents;width:120px;padding:10px;border:4px solid green;");
        f.Layout();
        var first = f.Source.DocumentTextBounds(0, 3).Single();
        var second = f.Source.DocumentTextBounds(3, 3).Single();
        var third = f.Source.DocumentTextBounds(6, 3).Single();
        Assert.Equal(first.Right, second.Left, 4);
        Assert.Equal(second.Right, third.Left, 4);
        f.HitOriginalOffsets();
        f.OriginalSource();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedDisplayAndTextEditsKeepOriginalOffsetsAndNodes(bool document)
    {
        using var f = new Fixture(document, "span");
        var source = f.Source;
        foreach (var mode in new[]
        {
            "grid",
            "flex",
            "inline",
            "block",
            "inline-block",
            "contents",
            "none",
            "inline"
        }
        )
        {
            f.Style($"display:{mode};gap:4px;grid-template-columns:36px 50px;");
            f.Layout();
            f.SameGeometry();
            f.OriginalSource();
            f.HitOriginalOffsets();
            Assert.Same(source, f.Source);
            f.Words(["AB ", "CD ", "EF ", "GH"]);
            f.Layout();
            f.SameGeometry();
            f.OriginalSource();
            f.HitOriginalOffsets();
        }

        f.Style("");
        f.Layout();
        f.SameGeometry();
        f.OriginalSource();
    }

    [Fact]
    public void ASharedDocumentKeepsIndependentInlineAllocationsAndSelections()
    {
        using var f = new Fixture(true, "span");
        using var second = new FlowDocumentPresentation();
        second.SetDocument(f.Document);
        var firstProxy = f.Source;
        foreach (var mode in new[]
        {
            "inline",
            "block",
            "inline-grid",
            "inline",
            "contents"
        }
        )
        {
            f.Style($"display:{mode};grid-template-columns:36px 48px;gap:5px;padding:2px;border:1px solid green;");
            f.Layout();
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue();
                second.Measure(new(90, double.PositiveInfinity));
                second.Arrange(new(0, 0, 90, second.DesiredSize.Height));
            }

            var expected = f.Source.DocumentTextBounds(3, 2).ToArray();
            var other = second.Paragraphs[0];
            var provider = ProviderFor(second);
            var found = provider.DocumentRange.FindText("BB", false, false)!;
            found.Select();
            Assert.Equal("BB", provider.GetSelection().Single().GetText(-1));
            Assert.NotEmpty(found.GetBoundingRectangles());
            f.Layout();
            Assert.Equal(expected, f.Source.DocumentTextBounds(3, 2));
            Assert.Same(firstProxy, f.Source);
            Assert.Same(f.Runs[1], f.Span.Inlines[0]);
            Assert.Same(f.Span.Inlines, ((Span)f.Originals[1]).Inlines);
            Assert.NotSame(f.Source, other);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyAndBreakBoxesContributeLayoutWithoutSelectableText(bool document)
    {
        var empty = new Span();
        var line = new LineBreak();
        var run = new Run("AB");
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(empty);
        paragraph.Inlines.Add(line);
        paragraph.Inlines.Add(run);
        var text = new TextBlock();
        FlowDocumentPresentation? body = null;
        if (document)
        {
            var doc = new FlowDocument
            {
                PagePadding = new(0),
                FontFamily = "Courier New",
                FontSize = 16,
                LineHeight = 24
            };
            doc.Blocks.Add(paragraph);
            Css.SetStyle(doc, Fixture.Font);
            Css.SetStyle(paragraph, "margin:0;padding:0;border:0;");
            body = new();
            body.SetDocument(doc);
        }
        else
        {
            paragraph.Inlines.Clear();
            text.Inlines.Add(empty);
            text.Inlines.Add(line);
            text.Inlines.Add(run);
            Css.SetStyle(text, Fixture.Font + "display:block;");
        }

        try
        {
            Css.SetStyle(empty, "display:block;height:30px;background:green;");
            Css.SetStyle(line, "display:inline;");
            var root = (FrameworkElement? )body ?? text;
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue();
                root.Measure(new(180, double.PositiveInfinity));
                root.Arrange(new(0, 0, 180, root.DesiredSize.Height));
            }

            var source = body?.Paragraphs[0] ?? text;
            Assert.Equal("\nAB", source.Text);
            Assert.Equal(78, body?.ExtentHeight ?? text.DesiredSize.Height, 4);
            Assert.Empty(source.DocumentTextBounds(0, 1));
            Assert.Equal(54, source.DocumentTextBounds(1, 2).Single().Y, 4);
            Assert.Same(run, source.Inlines[2]);
        }
        finally
        {
            body?.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeWidthRetainsPriorityOverInlineCssSizing(bool document)
    {
        using var f = new Fixture(document, "span");
        f.NativeWidth(130);
        f.Style("display:grid;width:60px;grid-template-columns:50% 50%;padding:3px;border:2px solid green;");
        f.Layout();
        f.SameGeometry();
        f.HitOriginalOffsets();
        f.OriginalSource();
    }

    public static IEnumerable<object[]> FragmentedBoxes()
    {
        foreach (var document in new[]
        {
            false,
            true
        }
        )
            foreach (var mode in new[]
            {
                "inline",
                "inline-block"
            }
            )
                foreach (var container in new[]
                {
                    "columns:2;column-gap:12px;height:48px;column-fill:auto;",
                    "line-clamp:2 no-ellipsis;width:90px;",
                    "width:90px;"
                }
                )
                    yield return[document, mode, container];
    }

    [Theory]
    [MemberData(nameof(FragmentedBoxes))]
    public void FragmentedInlineRangesMatchTheSharedFormatters(bool document, string mode, string container)
    {
        using var f = new Fixture(document, "span");
        f.Words(["AA BB CC ", "DD EE FF GG ", "HH II JJ KK ", "LL MM NN OO PP QQ RR"]);
        f.Style($"display:{mode};box-decoration-break:clone;padding:2px;border:1px solid green;");
        f.RootStyle(container);
        f.Layout();
        f.SameGeometry();
        f.OriginalSource();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineVisibilityIsComputedForEachOriginalDescendant(bool document)
    {
        using var f = new Fixture(document, "span");
        f.Style("display:inline;padding:3px;visibility:hidden;");
        f.NodeStyle(f.Runs[1], "display:inline;visibility:visible;");
        f.Layout();
        f.SameGeometry();
        f.OriginalSource();
        Assert.NotEmpty(f.Source.DocumentTextBounds(3, 2));
        Assert.Empty(f.Source.DocumentTextBounds(6, 2));
        f.HitOriginalOffsets();
        f.Style("display:none;");
        f.Layout();
        Assert.Empty(f.Source.DocumentTextBounds(3, 5));
        f.OriginalSource();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertedRunsRecomputeStructuralSelectorsWithoutReparenting(bool document)
    {
        var runs = new[]
        {
            new Run("AA"),
            new Run("BB"),
            new Run("CC")
        };
        var paragraph = new Paragraph();
        var text = new TextBlock();
        FlowDocumentPresentation? body = null;
        FlowDocument? doc = null;
        var collection = document ? paragraph.Inlines : text.Inlines;
        foreach (var run in runs)
            collection.Add(run);
        if (document)
        {
            doc = new FlowDocument
            {
                PagePadding = new(0),
                FontFamily = "Courier New",
                FontSize = 16,
                LineHeight = 24
            };
            doc.Blocks.Add(paragraph);
            Css.SetStyle(doc, Fixture.Font);
            Css.SetStyle(paragraph, "margin:0;padding:0;border:0;");
            body = new();
            body.SetDocument(doc);
        }
        else
            Css.SetStyle(text, Fixture.Font + "display:block;");
        Css.SetStyleSheet((DependencyObject? )doc ?? text, "Run{display:inline;}Run:nth-child(2){display:block;}");
        try
        {
            var root = (FrameworkElement? )body ?? text;
            void Layout()
            {
                for (var i = 0; i < 4; i++)
                {
                    Dispatcher.CurrentDispatcher.ProcessQueue();
                    root.Measure(new(180, double.PositiveInfinity));
                    root.Arrange(new(0, 0, 180, root.DesiredSize.Height));
                }
            }

            Layout();
            var source = body?.Paragraphs[0] ?? text;
            var initial = source.DocumentTextBounds(2, 2).Single();
            Assert.Equal(24, initial.Y, 4);
            var inserted = new Run("XX");
            collection.Insert(0, inserted);
            Layout();
            Assert.Equal(48, source.DocumentTextBounds(4, 2).Single().Y, 4);
            Assert.Equal("XXAABBCC", source.Text);
            collection.Remove(inserted);
            Layout();
            Assert.Equal(initial, source.DocumentTextBounds(2, 2).Single());
            Assert.Equal(runs, collection);
            Assert.Equal("AABBCC", source.Text);
            foreach (var run in runs)
                Assert.Same(document ? (DependencyObject)paragraph : text, ((FrameworkContentElement)run).Parent);
        }
        finally
        {
            body?.Dispose();
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("span")]
    public void PercentageRadiiRemainResolvedAgainstEachProjectedFragment(string target)
    {
        using var f = new Fixture(false, target);
        f.Style("display:inline;border-radius:50% / 20%;padding:3px;border:2px solid green;background:#eef5f0;");
        f.Layout();
        f.SameGeometry();
        f.OriginalSource();
        var source = target == "run" ? (DependencyObject)f.Runs[1] : f.Span;
        var radius = CssBorderRadiusProperties.Get(source);
        Assert.NotNull(radius);
        Assert.True(radius.UsesPercent);
        Assert.Equal(new Size(20, 4), radius.Resolve(new(40, 20)).TopLeft);
    }

    [Fact]
    public void ColumnSnapshotsRetainTheInlineDecorationState()
    {
        var root = new StackPanel();
        var prefix = new TextBlock
        {
            Text = "AA BB CC "
        };
        var styled = new TextBlock
        {
            Text = "DD EE FF GG "
        };
        var suffix = new TextBlock
        {
            Text = "HH II JJ KK LL MM NN OO"
        };
        foreach (var text in new[]
        {
            prefix,
            styled,
            suffix
        }
        )
        {
            root.Children.Add(text);
            Css.SetStyle(text, "display:inline;");
        }

        Css.SetStyle(root, Fixture.Font + "display:block;columns:2;column-gap:12px;height:48px;column-fill:auto;");
        Css.SetStyle(styled, "display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;background:#eef5f0;");
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue();
            root.Measure(new(180, double.PositiveInfinity));
            root.Arrange(new(0, 0, 180, 48));
        }

        Assert.True(styled.IsCssInlineBox);
        Assert.Equal(2, styled.CssDecorationFragments.Length);
        Assert.True(styled.CssDecorationFragments[1].Rect.Width < styled.CssDecorationFragments[0].Rect.Width);
        var rectangle = suffix.DocumentTextBounds(0, 2).First();
        var origin = suffix.TransformToAncestor(root);
        var x = (int)(origin.X + rectangle.X + 5);
        var y = (int)(origin.Y + rectangle.Bottom - 3);
        var bitmap = new RenderTargetBitmap(360, 100, 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(bitmap);
        drawing.DrawRectangle(Brushes.White, null, new(0, 0, 360, 100));
        root.Render(drawing);
        drawing.Close();
        var pixels = bitmap.GetPixelBuffer();
        var at = (y * 360 + x) * 4;
        Assert.Equal(Colors.White, Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]));
    }

    [Fact]
    public void ColumnBreaksKeepInlineInkWithItsTextLine()
    {
        using var fixture = new Fixture(false, "run");
        fixture.Words(["AA BB CC ", "DD EE FF GG ", "HH II JJ KK ", "LL MM NN OO PP QQ RR"]);
        fixture.Style("display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;background:#eef5f0;");
        fixture.RootStyle("columns:2;column-gap:12px;height:48px;column-fill:auto;");
        fixture.Layout();
        var rectangle = fixture.Source.DocumentTextBounds(0, 2).First();
        var bitmap = new RenderTargetBitmap(360, 100, 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(bitmap);
        drawing.DrawRectangle(Brushes.White, null, new(0, 0, 360, 100));
        fixture.Text.Render(drawing);
        drawing.Close();
        var pixels = bitmap.GetPixelBuffer();
        var at = ((int)(rectangle.Bottom - 2) * 360 + (int)(rectangle.Left + 2)) * 4;
        Assert.Equal(Colors.White, Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]));
        fixture.OriginalSource();
    }

    [Theory]
    [InlineData(false, "run", false)]
    [InlineData(false, "span", false)]
    [InlineData(true, "run", false)]
    [InlineData(true, "span", false)]
    [InlineData(false, "run", true)]
    [InlineData(false, "span", true)]
    [InlineData(true, "run", true)]
    [InlineData(true, "span", true)]
    public void InlineBoxesRetainTheirOriginalTextDecorations(bool document, string target, bool native)
    {
        using var fixture = new Fixture(document, target);
        fixture.Style("display:inline;" + (native ? "" : "text-decoration-line:underline;text-decoration-style:double;text-decoration-color:red;text-decoration-thickness:2px;"));
        if (native)
            (target == "run" ? (Inline)fixture.Runs[1] : fixture.Span).TextDecorations = new TextDecorationCollection
            {
                new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Brush = Brushes.Red,
                    Thickness = 2
                }
            };
        fixture.Layout();
        var capture = new DecorationCapture();
        ((FrameworkElement? )fixture.Body ?? fixture.Text).Render(capture);
        Assert.Equal((target == "run" ? 1 : 2) * (native ? 1 : 2), capture.Strokes.Count);
        Assert.All(capture.Strokes, stroke =>
        {
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(stroke.Pen.Brush).Color);
            Assert.Equal(2, stroke.Pen.Thickness);
            Assert.True(stroke.End.X > stroke.Start.X);
        });
        fixture.OriginalSource();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeOwnerDecorationsRemainVisibleAroundBoxedInlines(bool document)
    {
        using var fixture = new Fixture(document, "span");
        fixture.Style("display:inline;");
        var decorations = new TextDecorationCollection
        {
            new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Brush = Brushes.Red,
                Thickness = 2
            }
        };
        if (document)
            ((Paragraph)fixture.Document!.Blocks[0]).TextDecorations = decorations;
        else
            fixture.Text.TextDecorations = decorations;
        fixture.Layout();
        var capture = new DecorationCapture();
        ((FrameworkElement? )fixture.Body ?? fixture.Text).Render(capture);
        Assert.NotEmpty(capture.Strokes);
        Assert.All(capture.Strokes, stroke => Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(stroke.Pen.Brush).Color));
        fixture.OriginalSource();
    }

    private sealed class DecorationCapture : DrawingContextAdapter
    {
        internal readonly List<(Pen Pen, Point Start, Point End)> Strokes = [];
        public override void DrawText(FormattedText text, Point origin)
        {
        }

        public override void DrawLine(Pen pen, Point start, Point end) => Strokes.Add((pen, start, end));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double x, double y)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y)
        {
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
        }

        public override void DrawImage(ImageSource image, Rect rectangle)
        {
        }

        public override void PushClip(Geometry clip)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius radius)
        {
        }
    }

    [Fact]
    public void BorrowedViewsKeepOneAuthoredCssParentForEachInline()
    {
        using var fixture = new Fixture(true, "span");
        fixture.Style("display:inline;");
        fixture.Layout();
        var paragraph = (Paragraph)fixture.Document!.Blocks[0];
        Assert.Equal(fixture.Originals, CssNode.Get(paragraph).EnumerateChildren().Select(node => node.Target));
        Assert.DoesNotContain(CssNode.Get(fixture.Source).EnumerateChildren(), node => node.Target is Inline);
        fixture.OriginalSource();
    }

    internal sealed class Fixture : IDisposable
    {
        internal const string Font = "font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;color:#146342;width:180px;";
        internal readonly Run[] Runs = [new("AA "), new("BB "), new("CC "), new("DD")];
        internal readonly Span Span;
        internal readonly Span Inner;
        internal readonly Inline[] Originals;
        internal readonly TextBlock Text = new();
        internal readonly FlowDocument? Document;
        internal readonly FlowDocumentPresentation? Body;
        internal readonly StackPanel Reference = new();
        private readonly DependencyObject _target;
        private readonly Dictionary<DependencyObject, FrameworkElement> _boxes = [];
        private readonly Dictionary<Inline, DependencyObject?> _parents = [];
        private readonly TextBlock[] _texts = new TextBlock[4];
        private string _raw;
        internal TextBlock Source => Body?.Paragraphs[0] ?? Text;

        internal Fixture(bool document, string target)
        {
            Inner = new Span(Runs[2]);
            Span = new Span(Runs[1]);
            Span.Inlines.Add(Inner);
            Originals = [Runs[0], Span, Runs[3]];
            _target = target == "run" ? Runs[1] : Span;
            if (document)
            {
                var paragraph = new Paragraph();
                foreach (var inline in Originals)
                    paragraph.Inlines.Add(inline);
                Document = new FlowDocument
                {
                    PagePadding = new(0),
                    FontFamily = "Courier New",
                    FontSize = 16,
                    LineHeight = 24
                };
                Document.Blocks.Add(paragraph);
                Css.SetStyle(Document, Font);
                Css.SetStyle(paragraph, "margin:0;padding:0;border:0;");
                Body = new();
                Body.SetDocument(Document);
            }
            else
            {
                foreach (var inline in Originals)
                    Text.Inlines.Add(inline);
                Css.SetStyle(Text, Font + "display:block;");
            }

            foreach (var inline in Originals)
                Reference.Children.Add(Build(inline));
            Css.SetStyle(Reference, Font + "display:block;");
            FrameworkElement Build(Inline inline)
            {
                _parents[inline] = ((FrameworkContentElement)inline).Parent;
                FrameworkElement result;
                if (inline is Run run)
                {
                    var text = new TextBlock
                    {
                        Text = run.Text
                    };
                    _texts[Array.IndexOf(Runs, run)] = text;
                    result = text;
                }
                else
                {
                    var panel = new StackPanel();
                    foreach (var child in ((Span)inline).Inlines)
                        panel.Children.Add(Build(child));
                    result = panel;
                }

                Css.SetStyle(result, "display:inline;");
                _boxes[inline] = result;
                return result;
            }

            _raw = string.Concat(Runs.Select(run => run.Text));
            Layout();
        }

        internal void Style(string value)
        {
            Css.SetStyle(_target, value);
            Css.SetStyle(_boxes[_target], string.IsNullOrEmpty(value) ? "display:inline;" : value);
        }

        internal void RootStyle(string value)
        {
            Css.SetStyle((DependencyObject? )Document ?? Text, Font + "display:block;" + value);
            Css.SetStyle(Reference, Font + "display:block;" + value);
        }

        internal void NodeStyle(Inline source, string value)
        {
            Css.SetStyle(source, value);
            Css.SetStyle(_boxes[source], value);
        }

        internal void NativeWidth(double value)
        {
            _target.SetValue(FrameworkElement.WidthProperty, value);
            _boxes[_target].Width = value;
        }

        internal void Layout()
        {
            for (var pass = 0; pass < 4; pass++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue();
                CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                var root = (FrameworkElement? )Body ?? Text;
                root.Measure(new(180, double.PositiveInfinity));
                root.Arrange(new(0, 0, 180, root.DesiredSize.Height));
                Reference.Measure(new(180, double.PositiveInfinity));
                Reference.Arrange(new(0, 0, 180, Reference.DesiredSize.Height));
            }
        }

        internal void SameGeometry()
        {
            var actualHeight = Body?.ExtentHeight ?? Text.DesiredSize.Height;
            Assert.Equal(Reference.DesiredSize.Height, actualHeight, 4);
            var offset = 0;
            for (var index = 0; index < Runs.Length; index++)
            {
                var actual = Source.DocumentTextBounds(offset, Runs[index].Text.Length);
                var expected = _texts[index].IsVisible ? _texts[index].DocumentTextBounds(0, Runs[index].Text.Length) : Array.Empty<Rect>();
                var at = _texts[index].TransformToAncestor(Reference);
                Assert.Equal(expected.Count, actual.Count);
                for (var part = 0; part < actual.Count; part++)
                {
                    Assert.Equal(expected[part].X + at.X, actual[part].X + (Body is null ? 0 : Source.TransformToAncestor(Body).X), 4);
                    Assert.Equal(expected[part].Y + at.Y, actual[part].Y + (Body is null ? 0 : Source.TransformToAncestor(Body).Y), 4);
                    Assert.Equal(expected[part].Width, actual[part].Width, 4);
                    Assert.Equal(expected[part].Height, actual[part].Height, 4);
                }

                offset += Runs[index].Text.Length;
            }
        }

        internal void Words(string[] values)
        {
            for (var i = 0; i < 4; i++)
            {
                Runs[i].Text = values[i];
                _texts[i].Text = values[i];
            }

            _raw = string.Concat(values);
        }

        internal void HitOriginalOffsets()
        {
            var offset = 0;
            for (var i = 0; i < 4; i++)
            {
                var rectangles = Source.DocumentTextBounds(offset, Runs[i].Text.Length);
                if (rectangles.Count > 0)
                {
                    var first = rectangles[0];
                    Assert.Equal(offset, Source.DocumentCharacterFromPoint(new(first.Left + 1, first.Top + first.Height / 2)));
                }

                offset += Runs[i].Text.Length;
            }
        }

        internal void OriginalSource()
        {
            Assert.Equal(_raw, Source.Text);
            foreach (var pair in _parents)
                Assert.Same(pair.Value, ((FrameworkContentElement)pair.Key).Parent);
            Assert.Same(Runs[1], Span.Inlines[0]);
            Assert.Same(Inner, Span.Inlines[1]);
            Assert.Same(Runs[2], Inner.Inlines[0]);
        }

        public void Dispose() => Body?.Dispose();
    }
}
