using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssEllipticalShadowTests
{
    private sealed class RecordingEffectContext : DrawingContextAdapter,
        IOffsetDrawingContext, IEffectDrawingContext
    {
        public Point Offset { get; set; }
        internal float[]? EllipticalRadii;
        internal CornerRadius? CircularRadii;
        internal Thickness InsetBorder;

        public void BeginEffectCapture(float x, float y, float w, float h) { }
        public void EndEffectCapture() { }
        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float captureOriginX = 0, float captureOriginY = 0,
            float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
            => CircularRadii = new(cornerTL, cornerTR, cornerBR, cornerBL);

        public void ApplyElementEffectElliptical(IEffect effect, float x, float y, float w, float h,
            float captureOriginX, float captureOriginY,
            float topLeftX, float topLeftY, float topRightX, float topRightY,
            float bottomRightX, float bottomRightY, float bottomLeftX, float bottomLeftY)
            => EllipticalRadii = [topLeftX, topLeftY, topRightX, topRightY,
                bottomRightX, bottomRightY, bottomLeftX, bottomLeftY];

        public void ApplyElementEffectEllipticalWithBorder(IEffect effect,
            float x, float y, float w, float h, float captureOriginX, float captureOriginY,
            float topLeftX, float topLeftY, float topRightX, float topRightY,
            float bottomRightX, float bottomRightY, float bottomLeftX, float bottomLeftY,
            Thickness borderThickness)
        {
            InsetBorder = borderThickness;
            ApplyElementEffectElliptical(effect, x, y, w, h, captureOriginX, captureOriginY,
                topLeftX, topLeftY, topRightX, topRightY,
                bottomRightX, bottomRightY, bottomLeftX, bottomLeftY);
        }

        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    [Fact]
    public void VisualEffectCapture_PreservesBothCssAxesAndNativeTakeover()
    {
        var border = new Border
        {
            Background = Brushes.White,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0 },
        };
        Css.SetStyle(border, "border-radius:50px 20px / 10px 5px");
        CssBorderRadiusTests.Layout(border, 100, 40);

        var cssContext = new RecordingEffectContext();
        border.Render(cssContext);
        Assert.Equal(new float[] { 50, 10, 20, 5, 50, 10, 20, 5 }, cssContext.EllipticalRadii);
        Assert.Null(cssContext.CircularRadii);

        border.CornerRadius = new CornerRadius(7, 3, 9, 1);
        border.InvalidateVisual();
        var nativeContext = new RecordingEffectContext();
        border.Render(nativeContext);
        Assert.Null(nativeContext.EllipticalRadii);
        Assert.Equal(new CornerRadius(7, 3, 9, 1), nativeContext.CircularRadii);
    }

    [Fact]
    public void RecordedEffectCommand_RetainsAllEightRadii()
    {
        var effect = new DropShadowEffect();
        var command = Jalium.UI.Media.Rendering.DrawCommand.ApplyElementEffectEllipticalWithBorderCmd(
            effect, 1, 2, 3, 4, 5, 6,
            7, 8, 9, 10, 11, 12, 13, 14, new Thickness(2, 3, 4, 5));
        Assert.Equal(new double[] { 7, 8, 9, 10, 11, 12, 13, 14, 2, 3, 4, 5 },
            Assert.IsType<double[]>(command.C));
        Assert.Equal(1, command.V0);
        Assert.Equal(6, command.V5);

        var drawing = new Jalium.UI.Media.Rendering.RecordedDrawing([command], 1, null);
        var replay = new RecordingEffectContext();
        Jalium.UI.Media.Rendering.DrawingReplayer.Replay(drawing, replay);
        Assert.Equal(new float[] { 7, 8, 9, 10, 11, 12, 13, 14 }, replay.EllipticalRadii);
        Assert.Equal(new Thickness(2, 3, 4, 5), replay.InsetBorder);
    }

    [Fact]
    public void InsetEffectCapture_CarriesTheNativeBorderWidthsWithoutChangingCssRadii()
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(3, 5, 7, 9),
            Effect = new InnerShadowEffect
            {
                BlurRadius = 0,
                ShadowDepth = 0,
                SpreadRadius = 5,
            },
        };
        Css.SetStyle(border, "border-radius:45px 20px / 12px 8px");
        CssBorderRadiusTests.Layout(border, 100, 50);

        var context = new RecordingEffectContext();
        border.Render(context);
        Assert.Equal(new float[] { 45, 12, 20, 8, 45, 12, 20, 8 }, context.EllipticalRadii);
        Assert.Equal(new Thickness(3, 5, 7, 9), context.InsetBorder);
    }
}
