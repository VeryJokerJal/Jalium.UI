using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class MenuFlyoutSeparatorThemeTests
{
    [Fact]
    public void MenuFlyoutSeparator_OnRender_ShouldHonorForegroundBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var separator = new MenuFlyoutSeparator
        {
            Foreground = brush,
            Width = 120,
            Height = 9
        };

        separator.Measure(new Size(120, 9));
        separator.Arrange(new Rect(0, 0, 120, 9));

        var drawingContext = new RecordingDrawingContext();
        separator.Render(drawingContext);

        Assert.Same(brush, drawingContext.LastPen?.Brush);
    }

    private sealed class RecordingDrawingContext : DrawingContextAdapter
    {
        public Pen? LastPen { get; private set; }

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
            LastPen = pen;
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
}
