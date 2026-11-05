using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class SeparatorTemplateTests : IDisposable
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    public SeparatorTemplateTests()
    {
        Reset();
        Jalium.UI.Markup.ThemeLoader.Initialize();
        _ = new Application();
    }
    public void Dispose() => Reset();
    private static void Reset()
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        typeof(Application).GetField("_current", flags)!.SetValue(null, null);
        typeof(ThemeManager).GetField("_controlThemeDictionaries", flags)!.SetValue(null, Array.Empty<ResourceDictionary>());
        typeof(ThemeManager).GetMethod("Reset", flags)!.Invoke(null, null);
    }

    [Fact]
    public void DefaultLinesFollowOrientationThicknessAndZero()
    {
        var separator = Theme(new Separator { Margin = new Thickness(0) });
        Layout(separator, 100, 60);
        var horizontal = Assert.IsType<Border>(separator.FindName("PART_HorizontalLine"));
        var vertical = Assert.IsType<Border>(separator.FindName("PART_VerticalLine"));
        Assert.Equal(Visibility.Visible, horizontal.Visibility);
        Assert.Equal(Visibility.Collapsed, vertical.Visibility);
        Assert.Equal(new Size(100, 1), horizontal.RenderSize);
        separator.Orientation = Orientation.Vertical; separator.StrokeThickness = 4;
        Layout(separator, 100, 60);
        Assert.Equal(Visibility.Collapsed, horizontal.Visibility);
        Assert.Equal(Visibility.Visible, vertical.Visibility);
        Assert.Equal(new Size(4, 60), vertical.RenderSize);
        separator.StrokeThickness = 0; Layout(separator, 100, 60);
        Assert.Equal(0, vertical.RenderSize.Width);
        Assert.Throws<ArgumentException>(() => separator.StrokeThickness = -1);
        Assert.Throws<ArgumentException>(() => separator.StrokeThickness = double.NaN);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CssBoxAndBrushPriorityRetainExactContentCoordinates(bool vertical)
    {
        var separator = Theme(new Separator { Margin = new Thickness(0),
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal });
        Css.SetStyle(separator, "border:8px solid red;padding:4px 6px 3px 12px;color:lime");
        Layout(separator, 100, 60);
        var line = Assert.IsType<Border>(separator.FindName(vertical ? "PART_VerticalLine" : "PART_HorizontalLine"));
        var point = line.TransformToAncestor(separator);
        Assert.Equal(vertical ? 52.5 : 20, point.X);
        Assert.Equal(vertical ? 12 : 30, point.Y);
        Assert.Equal(vertical ? new Size(1, 37) : new Size(66, 1), line.RenderSize);
        Assert.Equal(Color.FromRgb(0, 255, 0), Assert.IsType<SolidColorBrush>(line.Background).Color);
        separator.StrokeBrush = Brushes.Blue;
        Assert.Same(Brushes.Blue, line.Background);
        separator.ClearValue(Separator.StrokeBrushProperty); Css.SetStyle(separator, "");
        Layout(separator, 100, 60);
        Assert.Same(separator.StrokeBrush, line.Background);
    }

    [Fact]
    public void MissingPartsAndRetiredBindingsDoNotDrawTheDefaultLine()
    {
        var separator = Theme(new Separator()); Layout(separator, 100, 60);
        var retired = Assert.IsType<Border>(separator.FindName("PART_HorizontalLine"));
        var custom = new ControlTemplate(typeof(Separator));
        custom.SetVisualTree(() => new Border { Background = Brushes.Blue });
        separator.Template = custom; Layout(separator, 100, 60);
        var retiredBrush = retired.Background;
        separator.StrokeBrush = Brushes.Red;
        Assert.Same(retiredBrush, retired.Background);
        var context = new Lines(); separator.Render(context); Assert.Equal(0, context.Count);
        separator.ClearValue(Control.TemplateProperty); Layout(separator, 100, 60);
        Assert.Same(Brushes.Red, Assert.IsType<Border>(separator.FindName("PART_HorizontalLine")).Background);
    }

    [Fact]
    public void AppBarIntrinsicHeightCompactAndCssEdgesRemainLive()
    {
        var separator = Theme(new AppBarSeparator { Margin = new Thickness(0) });
        separator.Measure(new Size(100, 100));
        Assert.Equal(new Size(1, 32), separator.DesiredSize);
        separator.IsCompact = true; separator.Measure(new Size(100, 100));
        Assert.Equal(new Size(1, 24), separator.DesiredSize);
        Css.SetStyle(separator, "border:2px solid red;padding:6px;color:lime");
        Layout(separator, 40, 60);
        var line = Assert.IsType<Border>(separator.FindName("PART_Line"));
        Assert.Equal(new Point(19.5, 12), line.TransformToAncestor(separator));
        Assert.Equal(new Size(1, 36), line.RenderSize);
        Assert.Equal(Color.FromRgb(0, 255, 0), Assert.IsType<SolidColorBrush>(line.Background).Color);
        Assert.Throws<InvalidOperationException>(() => separator.SetValue(AppBarSeparator.SeparatorHeightProperty, 999d));
    }

    [Fact]
    public void StatusBarContextStylesVerticalLinesAndKeepsAuthoredDirection()
    {
        var bar = Theme(new StatusBar());
        var automatic = new Separator();
        var authored = new Separator { Orientation = Orientation.Horizontal, Width = 20 };
        bar.Items.Add("Ready"); bar.Items.Add(automatic); bar.Items.Add("UTF-8"); bar.Items.Add(authored);
        Layout(bar, 360, 26);
        Assert.IsType<Style>(bar.TryFindResource(StatusBar.SeparatorStyleKey));
        Assert.Equal(Orientation.Vertical, automatic.Orientation);
        Assert.True(automatic.RenderSize.Width > 0);
        Assert.Equal(Orientation.Horizontal, authored.Orientation);
        Assert.False(automatic.HasLocalValue(Separator.OrientationProperty));
        var explicitStyle = new Style(typeof(Separator));
        explicitStyle.Setters.Add(new Setter(Separator.OrientationProperty, Orientation.Horizontal));
        automatic.Style = explicitStyle; Layout(bar, 360, 26);
        Assert.Equal(Orientation.Horizontal, automatic.Orientation);
    }

    private static T Theme<T>(T control) where T : Control
    {
        control.Resources.MergedDictionaries.Add(Defaults.Value); return control;
    }
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.InvalidateMeasure(); element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
    }
    private sealed class Lines : DrawingContextAdapter
    {
        public int Count;
        public override void DrawLine(Pen pen, Point a, Point b) => Count++;
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
