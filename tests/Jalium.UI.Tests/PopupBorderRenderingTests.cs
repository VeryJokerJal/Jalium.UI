using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class PopupBorderRenderingTests
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
            var presenter = new MenuFlyoutPresenter(new MenuFlyout());
            presenter.Measure(new Size(184, 44));
            presenter.Arrange(new Rect(0, 0, 184, 44));
            var drawing = new OutlineCapture();
            presenter.Render(drawing);

            Assert.True(drawing.Thickness > 0);
            var half = drawing.Thickness * .5;
            Assert.Equal(0, drawing.Rectangle.Left - half, 6);
            Assert.Equal(0, drawing.Rectangle.Top - half, 6);
            Assert.Equal(presenter.RenderSize.Width, drawing.Rectangle.Right + half, 6);
            Assert.Equal(presenter.RenderSize.Height, drawing.Rectangle.Bottom + half, 6);
            Assert.Equal(8, drawing.Radius + half, 6);
            Assert.Equal(Math.Round(dpi, MidpointRounding.AwayFromZero), drawing.Thickness * dpi, 6);
        }
        finally { FrameworkElement.LayoutDpiScale = previousDpi; }
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void RoundedBorder_LayoutClipAndStrokeUseTheSameDeviceWidth(double dpi)
    {
        var previousDpi = FrameworkElement.LayoutDpiScale;
        try
        {
            FrameworkElement.LayoutDpiScale = dpi;
            var child = new Border();
            var border = new Border
            {
                UseLayoutRounding = true, ClipToBounds = true, CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1), BorderBrush = Brushes.White, Child = child
            };
            border.Measure(new Size(180, 96));
            border.Arrange(new Rect(0, 0, 180, 96));
            var clip = Assert.IsType<RectangleGeometry>(border.GetLayoutClip());
            var drawing = new OutlineCapture();
            border.Render(drawing);

            var expectedWidth = Math.Round(dpi, MidpointRounding.AwayFromZero) / dpi;
            Assert.Equal(expectedWidth, drawing.Thickness, 6);
            Assert.Equal(expectedWidth, clip.Rect.Left, 6);
            Assert.Equal(expectedWidth, child.VisualBounds.X, 6);
            Assert.Equal(expectedWidth, child.VisualBounds.Y, 6);
            Assert.Equal(border.RenderSize.Width - 2 * expectedWidth, child.RenderSize.Width, 6);
        }
        finally { FrameworkElement.LayoutDpiScale = previousDpi; }
    }

    [Fact]
    public void ContentDialog_CenteredTemplateInheritsPixelAlignment()
    {
        var previousDpi = FrameworkElement.LayoutDpiScale;
        try
        {
            FrameworkElement.LayoutDpiScale = 1;
            AssertCenteredDialogPixelAlignment();
        }
        finally { FrameworkElement.LayoutDpiScale = previousDpi; }
    }

    private static void AssertCenteredDialogPixelAlignment()
    {
        var card = new Border { Width = 180, Height = 94, CornerRadius = new CornerRadius(14) };
        var centeringPanel = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        centeringPanel.Children.Add(card);
        var template = new ControlTemplate(typeof(ContentDialog));
        template.SetVisualTree(() => centeringPanel);
        var dialog = new ContentDialog { Template = template, Visibility = Visibility.Visible };
        dialog.Measure(new Size(400, 301));
        dialog.Arrange(new Rect(0, 0, 400, 301));

        Assert.True(card.UseLayoutRounding);
        Assert.Equal(Math.Round(centeringPanel.VisualBounds.Y), centeringPanel.VisualBounds.Y);
        Assert.Equal(104, centeringPanel.VisualBounds.Y);
    }

    [Fact]
    public void Border_WithoutLayoutRounding_PreservesFractionalGeometry()
    {
        var border = new Border
        {
            UseLayoutRounding = false, BorderThickness = new Thickness(1.25),
            BorderBrush = Brushes.White, CornerRadius = new CornerRadius(8)
        };
        border.Measure(new Size(180.25, 44.75));
        border.Arrange(new Rect(.25, .75, 180.25, 44.75));
        var drawing = new OutlineCapture();
        border.Render(drawing);
        Assert.Equal(.25, border.VisualBounds.X);
        Assert.Equal(.75, border.VisualBounds.Y);
        Assert.Equal(1.25, drawing.Thickness);
        Assert.Equal(180.25, border.RenderSize.Width);
    }

    private sealed class OutlineCapture : DrawingContextAdapter
    {
        public Rect Rectangle { get; private set; }
        public double Radius { get; private set; }
        public double Thickness { get; private set; }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            if (pen is null) return;
            Rectangle = rectangle;
            Radius = radiusX;
            Thickness = pen.Thickness;
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius radius) =>
            DrawRoundedRectangle(brush, pen, rectangle, radius.TopLeft, radius.TopLeft);

        public override void DrawLine(Pen pen, Point start, Point end) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
