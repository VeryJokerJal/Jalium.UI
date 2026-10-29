using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class IconElementTextOptionsTests
{
    [Fact]
    public void SymbolIcon_ForwardsInheritedTextRenderingOptions()
    {
        var icon = new TestSymbolIcon { Symbol = Symbol.Website };
        AssertTextOptionsReachFormattedText(icon, icon.RenderForTest);
    }

    [Fact]
    public void FontIcon_ForwardsInheritedTextRenderingOptions()
    {
        var icon = new TestFontIcon { Glyph = "\uEB41" };
        AssertTextOptionsReachFormattedText(icon, icon.RenderForTest);
    }

    private static void AssertTextOptionsReachFormattedText(
        FrameworkElement icon,
        Action<DrawingContext> render)
    {
        TextOptions.SetTextRenderingMode(icon, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(icon, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(icon, TextHintingMode.Animated);
        icon.Measure(new Size(20, 20));
        icon.Arrange(new Rect(0, 0, 20, 20));

        var context = new RecordingDrawingContext();
        render(context);

        var text = Assert.Single(context.Texts);
        Assert.Equal((int)TextRenderingMode.ClearType, text.TextRenderingMode);
        Assert.Equal((int)TextFormattingMode.Display, text.TextFormattingMode);
        Assert.Equal((int)TextHintingMode.Animated, text.TextHintingMode);
    }

    private sealed class TestSymbolIcon : SymbolIcon
    {
        public void RenderForTest(DrawingContext context) => OnRender(context);
    }

    private sealed class TestFontIcon : FontIcon
    {
        public void RenderForTest(DrawingContext context) => OnRender(context);
    }

    private sealed class RecordingDrawingContext : DrawingContextAdapter
    {
        public List<FormattedText> Texts { get; } = [];

        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(
            Brush? brush,
            Pen? pen,
            Rect rectangle,
            double radiusX,
            double radiusY) { }
        public override void DrawEllipse(
            Brush? brush,
            Pen? pen,
            Point center,
            double radiusX,
            double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) =>
            Texts.Add(formattedText);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(
            Rect rectangle,
            IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
