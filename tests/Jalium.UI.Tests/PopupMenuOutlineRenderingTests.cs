using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("Application")]
public partial class PopupBorderRenderingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MenuFlyout_OutlineFitsInsidePopupSurface(double dpi)
    {
        var previousDpi = FrameworkElement.LayoutDpiScale;
        try
        {
            FrameworkElement.LayoutDpiScale = dpi;
            ThemeLoader.Initialize();
            using var stream = new MemoryStream();
            var themes = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
            var presenter = new MenuFlyoutPresenter(new MenuFlyout())
            {
                Style = (Style)themes[typeof(MenuFlyoutPresenter)],
                BorderBrush = Brushes.White,
                Background = Brushes.Black
            };
            presenter.Measure(new Size(184, 44));
            presenter.Arrange(new Rect(0, 0, 184, 44));
            var drawing = new OutlineCapture();
            Assert.IsType<Border>(presenter.FindName("PART_PopupBorder")).Render(drawing);
            Assert.True(drawing.Thickness > 0);
            var half = drawing.Thickness * .5;
            Assert.Equal(0, drawing.Rectangle.Left - half, 6);
            Assert.Equal(0, drawing.Rectangle.Top - half, 6);
            Assert.Equal(presenter.RenderSize.Width, drawing.Rectangle.Right + half, 6);
            Assert.Equal(presenter.RenderSize.Height, drawing.Rectangle.Bottom + half, 6);
            Assert.Equal(8, drawing.Radius + half, 6);
            Assert.Equal(Math.Round(dpi, MidpointRounding.AwayFromZero), drawing.Thickness * dpi, 6);
        }
        finally
        {
            FrameworkElement.LayoutDpiScale = previousDpi;
        }
    }

    private sealed class OutlineCapture : DrawingContextAdapter
    {
        public Rect Rectangle { get; private set; }
        public double Radius { get; private set; }
        public double Thickness { get; private set; }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            if (pen is null)
                return;
            Rectangle = rectangle;
            Radius = radiusX;
            Thickness = pen.Thickness;
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius radius) => DrawRoundedRectangle(brush, pen, rectangle, radius.TopLeft, radius.TopLeft);
        public override void DrawLine(Pen pen, Point start, Point end)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius radius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry geometry)
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
