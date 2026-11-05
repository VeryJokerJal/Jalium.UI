using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MenuFlyoutSeparatorThemeTests : IDisposable
{
    public MenuFlyoutSeparatorThemeTests() => Reset();
    public void Dispose() => Reset();
    private static void Reset()
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        typeof(Application).GetField("_current", flags)!.SetValue(null, null);
        typeof(Jalium.UI.Controls.Themes.ThemeManager).GetField("_controlThemeDictionaries", flags)!
            .SetValue(null, Array.Empty<ResourceDictionary>());
        typeof(Jalium.UI.Controls.Themes.ThemeManager).GetMethod("Reset", flags)!.Invoke(null, null);
    }

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

        MenuFlyoutTemplateTestHelper.Apply(separator);
        separator.Margin = new Thickness(0);
        separator.Measure(new Size(120, 9));
        separator.Arrange(new Rect(0, 0, 120, 9));

        var drawingContext = new RecordingDrawingContext();
        separator.Render(drawingContext);

        var line = Assert.IsType<Border>(separator.FindName("PART_Line"));
        Assert.Same(brush, line.Background);
        Assert.Equal(new Point(12, 4), line.TransformToAncestor(separator));
        Assert.Equal(new Size(96, 1), line.RenderSize);
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
