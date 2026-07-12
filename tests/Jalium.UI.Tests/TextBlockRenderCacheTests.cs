using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class TextBlockRenderCacheTests
{
    [Fact]
    public void TextBlock_Render_ShouldReuseFormattedText_AcrossRepeatedRenders()
    {
        var textBlock = new TextBlock
        {
            Text = "Cache me",
            Width = 240,
            Height = 32
        };

        textBlock.Measure(new Size(240, 32));
        textBlock.Arrange(new Rect(0, 0, 240, 32));

        var firstContext = new TrackingDrawingContext();
        textBlock.Render(firstContext);
        var firstFormattedText = Assert.Single(firstContext.DrawnTexts);

        var secondContext = new TrackingDrawingContext();
        textBlock.Render(secondContext);
        var secondFormattedText = Assert.Single(secondContext.DrawnTexts);

        Assert.Same(firstFormattedText, secondFormattedText);
    }

    [Fact]
    public void TextBlock_Render_ShouldRebuildFormattedText_WhenTextChanges()
    {
        var textBlock = new TextBlock
        {
            Text = "Before",
            Width = 240,
            Height = 32
        };

        textBlock.Measure(new Size(240, 32));
        textBlock.Arrange(new Rect(0, 0, 240, 32));

        var firstContext = new TrackingDrawingContext();
        textBlock.Render(firstContext);
        var firstFormattedText = Assert.Single(firstContext.DrawnTexts);

        textBlock.Text = "After";
        textBlock.Measure(new Size(240, 32));
        textBlock.Arrange(new Rect(0, 0, 240, 32));

        var secondContext = new TrackingDrawingContext();
        textBlock.Render(secondContext);
        var secondFormattedText = Assert.Single(secondContext.DrawnTexts);

        Assert.NotSame(firstFormattedText, secondFormattedText);
        Assert.Equal("After", secondFormattedText.Text);
    }

    [Fact]
    public void TextBlock_Render_ShouldOnlyDrawVisibleLines_WhenClipped()
    {
        // The four lines are laid out from the top (VerticalAlignment.Top + content-
        // sized height) so "Line 1" sits at the very top; a 16px-tall clip band at the
        // top therefore contains only the first line and the viewport-culling path in
        // DrawTextLines must skip the other three.
        //
        // NOTE: TextBlock vertically centers its lines inside any *extra* height
        // (GetVerticalContentOffset). A fixed Height taller than the content would push
        // every line below a top-anchored clip band, so a faithful cull would correctly
        // drop all four — which is why this test keeps the box content-sized and
        // top-aligned instead of giving it an oversized Height.
        var textBlock = new TextBlock
        {
            Text = "Line 1\nLine 2\nLine 3\nLine 4",
            Width = 240,
            VerticalAlignment = VerticalAlignment.Top
        };

        textBlock.Measure(new Size(240, 120));
        textBlock.Arrange(new Rect(0, 0, 240, 120));

        var drawingContext = new ClippedTrackingDrawingContext(new Rect(0, 0, 240, 16));
        textBlock.Render(drawingContext);

        Assert.Single(drawingContext.DrawnTexts);
        Assert.Equal("Line 1", drawingContext.DrawnTexts[0].Text);
    }

    [Fact]
    public void TextBlock_Render_ShouldRebuildWrappedLines_WhenWidthChangesButLineCountStaysSame()
    {
        // Regression for the word-wrap clipping bug: a width-only relayout that
        // lands on the SAME line count but DIFFERENT break points must refresh
        // the per-line FormattedText cache. Previously the cache was rebuilt only
        // when the line *count* changed, so the wider fragments stayed cached and
        // were redrawn into the now-narrower box — clipping the overflow.
        //
        // "abcd abcd abcd abcd" at fontSize 10:
        //   width 90 -> 3 words + 1  => ["abcd abcd abcd ", "abcd"]
        //   width 60 -> 2 words + 2  => ["abcd abcd ", "abcd abcd"]
        // Both are two lines; the per-word split (3+1 vs 2+2) is identical under
        // the headless estimate measurer and a real DirectWrite context, so the
        // expected fragments are stable regardless of which one is active.
        var textBlock = new TextBlock
        {
            Text = "abcd abcd abcd abcd",
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };

        textBlock.Measure(new Size(90, double.PositiveInfinity));
        textBlock.Arrange(new Rect(0, 0, 90, 64));
        var wideContext = new TrackingDrawingContext();
        textBlock.Render(wideContext);

        Assert.Equal(
            new[] { "abcd abcd abcd ", "abcd" },
            wideContext.DrawnTexts.Select(t => t.Text));

        textBlock.InvalidateMeasure();
        textBlock.Measure(new Size(60, double.PositiveInfinity));
        textBlock.Arrange(new Rect(0, 0, 60, 64));
        var narrowContext = new TrackingDrawingContext();
        textBlock.Render(narrowContext);

        // The cache must reflect the NEW (narrow) break points. Before the fix
        // this drew the stale ["abcd abcd abcd ", "abcd"] and clipped line 1.
        Assert.Equal(
            new[] { "abcd abcd ", "abcd abcd" },
            narrowContext.DrawnTexts.Select(t => t.Text));
    }

    private class TrackingDrawingContext : DrawingContextAdapter
    {
        public List<FormattedText> DrawnTexts { get; } = [];

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
            DrawnTexts.Add(formattedText);
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }

    private sealed class ClippedTrackingDrawingContext : TrackingDrawingContext, IClipBoundsDrawingContext, IOffsetDrawingContext
    {
        private readonly Stack<Rect?> _clipBounds = new();

        public ClippedTrackingDrawingContext(Rect initialClip)
        {
            _clipBounds.Push(initialClip);
        }

        public Point Offset { get; set; }

        public Rect? CurrentClipBounds => _clipBounds.Count > 0 ? _clipBounds.Peek() : null;

        public override void PushClip(Geometry clipGeometry)
        {
            var clipRect = clipGeometry.Bounds;
            clipRect = new Rect(
                clipRect.X + Offset.X,
                clipRect.Y + Offset.Y,
                clipRect.Width,
                clipRect.Height);

            var current = _clipBounds.Count > 0 ? _clipBounds.Peek() : null;
            _clipBounds.Push(current.HasValue ? Rect.Intersect(current.Value, clipRect) : clipRect);
        }

        public override void Pop()
        {
            if (_clipBounds.Count > 1)
            {
                _clipBounds.Pop();
            }
        }
    }
}
