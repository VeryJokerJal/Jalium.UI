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
        var textBlock = new TextBlock
        {
            Text = "Line 1\nLine 2\nLine 3\nLine 4",
            Width = 240,
            Height = 120
        };

        textBlock.Measure(new Size(240, 120));
        textBlock.Arrange(new Rect(0, 0, 240, 120));

        var drawingContext = new ClippedTrackingDrawingContext(new Rect(0, 0, 240, 16));
        textBlock.Render(drawingContext);

        Assert.Single(drawingContext.DrawnTexts);
        Assert.Equal("Line 1", drawingContext.DrawnTexts[0].Text);
    }

    private class TrackingDrawingContext : DrawingContext
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
            _clipBounds.Push(current.HasValue ? current.Value.Intersect(clipRect) : clipRect);
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
