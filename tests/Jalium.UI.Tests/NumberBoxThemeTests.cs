using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Controls.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class NumberBoxThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void NumberBox_ImplicitThemeStyle_ShouldApplyWithoutLocalHeightOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var numberBox = new NumberBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(numberBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(NumberBox), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(numberBox.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.NotNull(numberBox.SelectionBrush);
            Assert.NotNull(numberBox.CaretBrush);
            Assert.Equal(32, numberBox.MinHeight);
            Assert.True(numberBox.RenderSize.Height >= 32);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NumberBox_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("TextPlaceholder", out var placeholderObj));
            Assert.True(app.Resources.TryGetValue("TextSecondary", out var secondaryObj));
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedObj));
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);
            var caretBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var numberBox = new NumberBox();

            var placeholderMethod = typeof(NumberBox).GetMethod("ResolvePlaceholderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var secondaryMethod = typeof(NumberBox).GetMethod("ResolveSecondaryTextBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var focusedMethod = typeof(NumberBox).GetMethod("ResolveFocusedBorderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var selectionMethod = typeof(TextBoxBase).GetMethod("ResolveSelectionBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var caretMethod = typeof(TextBoxBase).GetMethod("ResolveCaretBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(placeholderMethod);
            Assert.NotNull(secondaryMethod);
            Assert.NotNull(focusedMethod);
            Assert.NotNull(selectionMethod);
            Assert.NotNull(caretMethod);

            Assert.Same(placeholderObj, placeholderMethod!.Invoke(numberBox, null));
            Assert.Same(secondaryObj, secondaryMethod!.Invoke(numberBox, null));
            Assert.Same(focusedObj, focusedMethod!.Invoke(numberBox, null));
            Assert.Same(selectionBrush, selectionMethod!.Invoke(numberBox, null));
            Assert.Same(caretBrush, caretMethod!.Invoke(numberBox, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NumberBox_SpinButtons_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);
            var secondaryText = Assert.IsAssignableFrom<Brush>(app.Resources["TextSecondary"]);

            var numberBox = new NumberBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(numberBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            var upSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_UpSpinButton"));
            var downSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_DownSpinButton"));

            Assert.Same(controlBackground, upSpinButton.Background);
            Assert.Same(controlBackground, downSpinButton.Background);

            var upPath = Assert.IsType<ShapePath>(upSpinButton.Content);
            var downPath = Assert.IsType<ShapePath>(downSpinButton.Content);

            Assert.Same(secondaryText, upPath.Stroke);
            Assert.Same(secondaryText, downPath.Stroke);
            Assert.Same(Jalium.UI.Input.Cursors.Arrow, upSpinButton.Cursor);
            Assert.Same(Jalium.UI.Input.Cursors.Arrow, downSpinButton.Cursor);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // Regression guard for the "上下按键不对齐" report: the up/down spinner
    // chevrons must render horizontally centered on the same axis AND as exact
    // vertical mirrors of each other. The original template drew them with
    // Stretch="None", whose natural size only reserves the stroke margin on the
    // bottom/right edges (Path.GetNaturalSize). For a mirrored chevron pair that
    // pushed the up glyph's apex (sitting on the geometry's top edge, with no
    // reserved margin) and the down glyph's apex (sitting on the bottom edge,
    // inside the reserved margin) into asymmetric positions — a ~0.75px vertical
    // skew between the two arrows. Routing the glyphs through Stretch="Uniform"
    // (which reserves strokeThickness/2 on every side and centers the content,
    // the same path every other arrow glyph in the framework uses) makes the
    // pair symmetric again.
    [Fact]
    public void NumberBox_SpinButtonChevrons_ShouldRenderHorizontallyCenteredVerticalMirrors()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var numberBox = new NumberBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(numberBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            var upSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_UpSpinButton"));
            var downSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_DownSpinButton"));
            var upPath = Assert.IsType<ShapePath>(upSpinButton.Content);
            var downPath = Assert.IsType<ShapePath>(downSpinButton.Content);

            // Both glyphs must share an identical render box so they are processed
            // identically; otherwise their centring offsets would diverge.
            Assert.Equal(upPath.RenderSize.Width, downPath.RenderSize.Width, 3);
            Assert.Equal(upPath.RenderSize.Height, downPath.RenderSize.Height, 3);
            Assert.True(upPath.RenderSize.Height > 0, "spinner chevron was not arranged");

            var upApex = ChevronApex(upPath);
            var downApex = ChevronApex(downPath);

            var height = upPath.RenderSize.Height;
            var width = upPath.RenderSize.Width;

            // Horizontally aligned: both apexes sit on the same vertical axis,
            // which is the horizontal centre of the glyph box.
            Assert.Equal(width / 2, upApex.X, 2);
            Assert.Equal(width / 2, downApex.X, 2);
            Assert.Equal(upApex.X, downApex.X, 3);

            // Vertically mirrored: the up apex (near the top) and the down apex
            // (near the bottom) are equidistant from the box centre, i.e. their
            // y-coordinates sum to the full box height.
            Assert.Equal(height, upApex.Y + downApex.Y, 2);
            Assert.True(upApex.Y < height / 2, "up chevron apex should point upward (above centre)");
            Assert.True(downApex.Y > height / 2, "down chevron apex should point downward (below centre)");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // Regression guard for the "spin border 偏移" report: the up/down spin buttons
    // must carry identical border edges. UpdateTemplateCornerRadii originally gave
    // the up button a LEFT border (the divider between the text area and the spin
    // column) but the down button a RIGHT border, so only the top half's background
    // was inset — stepping the column's left edge by 1px between the two halves.
    [Fact]
    public void NumberBox_SpinButtons_ShouldHaveMatchingBorderThickness()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var numberBox = new NumberBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(numberBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            var upSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_UpSpinButton"));
            var downSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_DownSpinButton"));

            var up = upSpinButton.BorderThickness;
            var down = downSpinButton.BorderThickness;

            // Identical edges → one straight, full-height divider; no 1px step.
            Assert.Equal(up.Left, down.Left, 3);
            Assert.Equal(up.Top, down.Top, 3);
            Assert.Equal(up.Right, down.Right, 3);
            Assert.Equal(up.Bottom, down.Bottom, 3);

            // The divider lives on the left edge only; the right edge stays flush so
            // both halves reach the rounded outer corner identically.
            Assert.True(up.Left > 0, "spin column should carry a left divider border");
            Assert.Equal(0, up.Right, 3);
            Assert.Equal(0, up.Top, 3);
            Assert.Equal(0, up.Bottom, 3);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// Renders the spinner <see cref="ShapePath"/> through its (protected) OnRender
    /// into a recording context and returns the baked, post-stretch apex vertex of
    /// the chevron (the middle vertex of the "M x,y L apex L x,y" polyline) in the
    /// path's local render space.
    /// </summary>
    private static Point ChevronApex(ShapePath path)
    {
        var recorder = new GeometryRecordingContext();
        var onRender = typeof(ShapePath).GetMethod("OnRender",
            BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(DrawingContext) });
        Assert.NotNull(onRender);
        onRender!.Invoke(path, new object[] { recorder });

        var geometry = Assert.IsType<PathGeometry>(recorder.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var apexSegment = Assert.IsType<LineSegment>(figure.Segments[0]);
        return apexSegment.Point;
    }

    private sealed class GeometryRecordingContext : DrawingContextAdapter
    {
        public Geometry? LastGeometry { get; private set; }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => LastGeometry = geometry;
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
