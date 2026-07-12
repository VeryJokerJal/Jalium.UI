using Jalium.UI.Controls;
using Jalium.UI.Media.Effects;
using DrawingContext = Jalium.UI.Media.DrawingContext;
using DrawingContextAdapter = Jalium.UI.Media.DrawingContextAdapter;
using Pen = Jalium.UI.Media.Pen;
using Brush = Jalium.UI.Media.Brush;
using Geometry = Jalium.UI.Media.Geometry;
using Transform = Jalium.UI.Media.Transform;
using ImageSource = Jalium.UI.Media.ImageSource;
using FormattedText = Jalium.UI.Media.FormattedText;
using IBackdropEffect = Jalium.UI.IBackdropEffect;

namespace Jalium.UI.Tests;

public class EffectCaptureBoundsTests
{
    [Fact]
    public void Render_WithElementEffect_SnapsCaptureBoundsToPixelGrid()
    {
        var border = new Border
        {
            Width = 100,
            Height = 50,
            Effect = new BlurEffect(2.4)
        };

        border.Measure(new Size(100, 50));
        border.Arrange(new Rect(0, 0, 100, 50));

        var context = new RecordingEffectContext
        {
            Offset = new Point(10.25, 20.75)
        };

        border.Render(context);

        Assert.Single(context.BeginCalls);
        Assert.Single(context.ApplyCalls);

        var begin = context.BeginCalls[0];
        var apply = context.ApplyCalls[0];

        Assert.Equal(7f, begin.X);
        Assert.Equal(18f, begin.Y);
        Assert.Equal(106f, begin.Width);
        Assert.Equal(56f, begin.Height);

        Assert.Equal(begin, apply);
    }

    private sealed class RecordingEffectContext : DrawingContextAdapter, IOffsetDrawingContext, IEffectDrawingContext
    {
        public Point Offset { get; set; }

        public List<CaptureBounds> BeginCalls { get; } = [];
        public List<CaptureBounds> ApplyCalls { get; } = [];

        public void BeginEffectCapture(float x, float y, float w, float h)
        {
            BeginCalls.Add(new CaptureBounds(x, y, w, h));
        }

        public void EndEffectCapture()
        {
        }

        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
        {
            ApplyCalls.Add(new CaptureBounds(x, y, w, h));
        }

        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h, float captureOriginX = 0, float captureOriginY = 0, float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
        {
            // Interface-conforming overload. Visual.RenderDirect dispatches to
            // this 11-arg variant, supplying element bounds in (x,y,w,h) and
            // the snapped capture origin in (captureOriginX,captureOriginY).
            // Reconstruct the snapped capture extents from the effect padding
            // so the test can compare BeginEffectCapture and ApplyElementEffect
            // against the same rectangle.
            var padding = effect.EffectPadding;
            var snappedRight = (float)Math.Ceiling(x + w + padding.Right);
            var snappedBottom = (float)Math.Ceiling(y + h + padding.Bottom);
            var capW = snappedRight - captureOriginX;
            var capH = snappedBottom - captureOriginY;
            ApplyCalls.Add(new CaptureBounds(captureOriginX, captureOriginY, capW, capH));
        }

        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private readonly record struct CaptureBounds(float X, float Y, float Width, float Height);
}
