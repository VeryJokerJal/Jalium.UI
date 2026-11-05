using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests.RenderThread;

public sealed class GradientTextRecordingTests : IDisposable
{
    public void Dispose() => DrawingContext.EndWholeFrameRecordingScope((false, false));

    private static GradientBrush Brush(bool radial) => radial
        ? new RadialGradientBrush(Colors.Red, Colors.Blue)
        { Center = new(.4, .6), GradientOrigin = new(.3, .4), RadiusX = .7, RadiusY = .5 }
        : new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(.1, .2), new Point(.9, .8));

    private static RecordedDrawing Record(FormattedText text, bool perVisual = false)
    {
        var recorder = new DrawingRecorder();
        if (perVisual) recorder.Bind(new RecordingRenderSink());
        else recorder.BindWholeFrame();
        recorder.DrawText(text, new Point(8, 12));
        return recorder.Commit();
    }

    private static FormattedText Text(GradientBrush brush) => new("A\u0301 中文 office", "Test Sans", 18)
    { Foreground = brush, MaxTextWidth = 180, MaxTextHeight = 80 };

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task GradientTextRetainsAnImmutableWorkerReadablePaint(bool radial, bool perVisual)
    {
        var source = Brush(radial);
        var transform = new TranslateTransform(4, 7);
        source.Transform = transform;
        source.RelativeTransform = new ScaleTransform(.8, 1.2);
        source.Opacity = .6;
        var text = Text(source);
        text.GlyphRanges = [0, 2]; text.GlyphVisibility = 1;
        var drawing = Record(text, perVisual);
        Assert.True(drawing.IsFullyRecordable);
        var snapshot = Assert.IsType<FormattedText>(Assert.Single(drawing.Commands).A);
        var paint = Assert.IsAssignableFrom<GradientBrush>(snapshot.Foreground);
        Assert.NotSame(source, paint);
        Assert.True(paint.IsFrozen);
        Assert.True(paint.GradientStops.IsFrozen);
        Assert.All(paint.GradientStops, stop => Assert.True(stop.IsFrozen));
        Assert.True(paint.Transform!.IsFrozen);
        Assert.True(paint.RelativeTransform!.IsFrozen);
        Assert.False(source.IsFrozen);
        Assert.False(text.IsMeasured);
        source.GradientStops[0].Color = Colors.Green;
        source.GradientStops.Add(new GradientStop(Colors.Yellow, .5));
        source.Opacity = .2; transform.X = 40;
        text.Foreground = new SolidColorBrush(Colors.Black);
        text.GlyphRanges[0] = 4; text.MaxTextWidth = 60;
        var ownerThread = Environment.CurrentManagedThreadId;
        await OnWorker(() =>
        {
            Assert.NotEqual(ownerThread, Environment.CurrentManagedThreadId);
            Assert.Equal(Colors.Red, paint.GradientStops[0].Color);
            Assert.Equal(2, paint.GradientStops.Count);
            Assert.Equal(.6, paint.Opacity);
            Assert.Equal(4, paint.Transform.Value.OffsetX);
            Assert.Equal(new uint[] { 0, 2 }, snapshot.GlyphRanges);
            Assert.Equal(180, snapshot.MaxTextWidth);
            Assert.Throws<InvalidOperationException>(() => paint.GradientStops[0].Color = Colors.Black);
            var sink = new RecordingRenderSink();
            DrawingReplayer.Replay(drawing, sink);
            Assert.Single(sink.Events, entry => entry.StartsWith("DrawText"));
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnchangedTextKeepsTheGradientCacheIdentityWhileMutationsPublishNewPaint(bool radial)
    {
        var source = Brush(radial); var text = Text(source);
        var first = Assert.IsType<FormattedText>(Record(text).Commands[0].A);
        var second = Assert.IsType<FormattedText>(Record(text).Commands[0].A);
        Assert.Same(first.Foreground, second.Foreground);
        source.GradientStops[1].Offset = .8;
        var changed = Assert.IsType<FormattedText>(Record(text).Commands[0].A);
        Assert.NotSame(first.Foreground, changed.Foreground);
        Assert.Equal(1, ((GradientBrush)first.Foreground!).GradientStops[1].Offset);
        Assert.Equal(.8, ((GradientBrush)changed.Foreground!).GradientStops[1].Offset);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CssUsedSizeLayoutSurvivesSnapshotsAndLayoutReplacement(bool radial)
    {
        var reader = new CssTokenReader(radial ? "ellipse at 30% 40%, red 12px, blue 90%" : "35deg, red 12px, blue 90%");
        Assert.True(CssGradientParser.TryParseGradientFunction(radial ? "radial-gradient" : "linear-gradient", ref reader, out var parsed));
        var source = Assert.IsAssignableFrom<GradientBrush>(parsed);
        var first = Assert.IsAssignableFrom<GradientBrush>(DrawInputSnapshotter.SnapshotBrush(source));
        Assert.NotNull(first.CssImageLayout);
        Assert.NotSame(source.CssImageLayout, first.CssImageLayout);
        Assert.True(first.IsFrozen);
        source.CssImageLayout = new CssGradientLayout(radial ? "radial-gradient" : "linear-gradient", "red, green", new CssLengthContext(14, 14, 14, 100, 100));
        var changed = DrawInputSnapshotter.SnapshotBrush(source);
        Assert.NotSame(first, changed);
        Assert.NotSame(first.CssImageLayout, changed!.CssImageLayout);
        var resolved = CssImageLayout.ResolveBrush(first, 180, 70);
        Assert.NotNull(resolved);
        Assert.True(resolved.IsFrozen);
    }

    [Fact]
    public async Task CssGradientCapturesTreeRulersWhileKeepingItsUsedSizeFlexible()
    {
        var target = new Jalium.UI.Controls.Border();
        var root = new Jalium.UI.Controls.StackPanel();
        root.Children.Add(new Jalium.UI.Controls.Border()); root.Children.Add(target);
        var context = new CssLengthContext(18, 16, 14, 800, 600, element: CssNode.Get(target));
        var source = Brush(false);
        source.CssImageLayout = new CssGradientLayout("linear-gradient",
            "to right, red calc(10px * sibling-index()), blue calc(1em + 50%)", context);
        var snapshot = Assert.IsAssignableFrom<GradientBrush>(DrawInputSnapshotter.SnapshotBrush(source));
        root.Children.Insert(0, new Jalium.UI.Controls.Border());
        var changed = Assert.IsAssignableFrom<GradientBrush>(DrawInputSnapshotter.SnapshotBrush(source));
        Assert.NotSame(snapshot, changed);
        await OnWorker(() =>
        {
            foreach (var width in new[] { 180d, 360d })
            {
                var oldPaint = Assert.IsAssignableFrom<GradientBrush>(CssImageLayout.ResolveBrush(snapshot, width, 70));
                var newPaint = Assert.IsAssignableFrom<GradientBrush>(CssImageLayout.ResolveBrush(changed, width, 70));
                Assert.Equal(20 / width, oldPaint.GradientStops[0].Offset, 8);
                Assert.Equal(30 / width, newPaint.GradientStops[0].Offset, 8);
                Assert.Equal(.5 + 18 / width, oldPaint.GradientStops[^1].Offset, 8);
                Assert.True(oldPaint.IsFrozen);
            }
        });
    }

    [Fact]
    public async Task CapturedLengthContextHasNoLiveTreeOrRulerReferences()
    {
        var target = new Jalium.UI.Controls.Border(); var root = new Jalium.UI.Controls.StackPanel();
        root.Children.Add(target); VisualTreeHelper.SetRootDpi(root, new(2, 2));
        var original = new CssLengthContext(18, 16, 14, 800, 600, element: CssNode.Get(target));
        var snapshot = original.CaptureForRendering();
        Assert.Null(snapshot.Element); Assert.Null(snapshot.Dependent); Assert.Null(snapshot.Containers);
        Assert.Null(snapshot.Fonts); Assert.Null(snapshot.Viewports);
        root.Children.Insert(0, new Jalium.UI.Controls.Border()); VisualTreeHelper.SetRootDpi(root, new(1, 1));
        await OnWorker(() =>
        {
            Assert.True(new CssLength(2, CssUnit.Em).TryResolve(snapshot, CssPercentBasis.NotSupported, out var em));
            Assert.Equal(36, em);
            Assert.True(new CssLength(10, CssUnit.Cqw).TryResolve(snapshot, CssPercentBasis.NotSupported, out var cqw));
            Assert.Equal(80, cqw);
            var sibling = new CssMathExpression("sibling-index", CssNumericType.Of(CssNumericKind.Number), default, []);
            Assert.True(sibling.TryEvaluate(snapshot, 0, out var index)); Assert.Equal(1, index);
            Assert.Equal(2, snapshot.RenderSnapshot!.DpiScale);
        });
    }

    [Fact]
    public void CapturedLengthRulersPreserveLiveArithmeticAndSpecialValues()
    {
        var live = new CssLengthContext(18, 16, 14, 803.25, 607.5, provisionalTreeCounts: true);
        var captured = live.CaptureForRendering();
        foreach (var unit in Enum.GetValues<CssUnit>().Where(unit => unit is not (CssUnit.Expression or CssUnit.Percent)))
        foreach (var value in new[] { 0d, -3d, 1.25, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var length = new CssLength(value, unit);
            var valid = length.TryResolve(live, CssPercentBasis.NotSupported, out var expected);
            Assert.Equal(valid, length.TryResolve(captured, CssPercentBasis.NotSupported, out var actual));
            if (!valid) continue;
            if (!double.IsFinite(expected)) Assert.Equal(expected, actual);
            else Assert.Equal(expected, actual, 10);
        }
    }

    [Fact]
    public async Task RangePaintsAreDetachedEvenWithASolidTopLevelForeground()
    {
        var gradient = Brush(false); var solid = new SolidColorBrush(Colors.Red);
        var text = new FormattedText("abcdef", "Test Sans", 18) { Foreground = solid };
        text.SetForegroundBrush(gradient, 2, 2);
        var drawing = Record(text);
        Assert.True(drawing.IsFullyRecordable);
        var captured = Assert.IsType<FormattedText>(drawing.Commands[0].A);
        var before = RangeBrushes(captured);
        Assert.All(before, brush => Assert.True(brush!.IsFrozen));
        Assert.Same(before[0], before[1]); Assert.Same(before[2], before[3]);
        gradient.GradientStops[0].Color = Colors.Green; solid.Color = Colors.Yellow;
        text.SetForegroundBrush(new SolidColorBrush(Colors.Black), 2, 2);
        await OnWorker(() =>
        {
            Assert.Equal(Colors.Red, ((SolidColorBrush)before[0]!).Color);
            Assert.Equal(Colors.Red, ((GradientBrush)before[2]!).GradientStops[0].Color);
        });
    }

    [Fact]
    public void RangeChangesWithEqualScalarFieldsDoNotReuseOldFormatting()
    {
        var text = new FormattedText("abcdef", "Test Sans", 18) { Foreground = new SolidColorBrush(Colors.Red) };
        text.SetForegroundBrush(new SolidColorBrush(Colors.Blue), 2, 2);
        var first = Assert.IsType<FormattedText>(Record(text).Commands[0].A);
        text.SetForegroundBrush(new SolidColorBrush(Colors.Green), 2, 2);
        var changed = Assert.IsType<FormattedText>(Record(text).Commands[0].A);
        Assert.Equal(Colors.Blue, ((SolidColorBrush)RangeBrushes(first)[2]!).Color);
        Assert.Equal(Colors.Green, ((SolidColorBrush)RangeBrushes(changed)[2]!).Color);
    }

    [Fact]
    public void SimpleSolidTextStillUsesTheFrozenValuePool()
    {
        var text = new FormattedText("plain", "Test Sans", 18) { Foreground = new SolidColorBrush(Colors.Red) };
        var first = Assert.IsType<FormattedText>(Record(text).Commands[0].A);
        var second = Assert.IsType<FormattedText>(Record(text).Commands[0].A);
        Assert.Same(first, second); Assert.True(first.Foreground!.IsFrozen);
        Assert.False(text.Foreground!.IsFrozen);
    }

    [Fact]
    public void UnsupportedImageTextStillRequiresDirectPaint()
    {
        var text = new FormattedText("image", "Test Sans", 18) { Foreground = new ImageBrush() };
        Assert.False(Record(text).IsFullyRecordable);
    }

    private static Brush?[] RangeBrushes(FormattedText text)
    {
        var formats = (Array)typeof(FormattedText).GetField("_characterFormats", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(text)!;
        return formats.Cast<object>().Select(format => (Brush?)format.GetType().GetProperty("Foreground")!.GetValue(format)).ToArray();
    }

    private static Task OnWorker(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() => { try { action(); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } }) { IsBackground = true }.Start();
        return completion.Task;
    }
}
