using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ContentDialogRenderingTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void CssShadowLayers_ShouldPreservePaintOrder_AndFallback(bool backgroundOnly, bool supportsLayers)
    {
        var child = new PaintMarker(Brushes.Lime);
        var shadow = new DropShadowEffect { BlurRadius = 12, CssBoxShadowLayer = true };
        FrameworkElement root = backgroundOnly
            ? new Border { Background = Brushes.Blue, Child = child, Effect = shadow }
            : new PaintMarker(Brushes.Blue, child) { Effect = shadow };
        root.Measure(new Size(100, 60));
        root.Arrange(new Rect(0, 0, 100, 60));
        var sink = new BackdropSink { SupportsCssShadowLayers = supportsLayers };
        root.Render(sink);
        if (supportsLayers)
        {
            Assert.DoesNotContain("capture", sink.Trace);
            Assert.Equal(backgroundOnly
                ? new[] { "outer", "background", "inset", "child" }
                : new[] { "outer", "background", "child", "inset" }, sink.Trace);
        }
        else
        {
            Assert.Contains("capture", sink.Trace);
            Assert.Contains("apply", sink.Trace);
            Assert.DoesNotContain("outer", sink.Trace);
        }
    }

    private sealed class PaintMarker : FrameworkElement
    {
        private readonly Brush _brush;
        private readonly UIElement? _child;
        public PaintMarker(Brush brush, UIElement? child = null)
        {
            _brush = brush;
            _child = child;
            if (child != null) AddVisualChild(child);
        }
        protected override int VisualChildrenCount => _child == null ? 0 : 1;
        protected override Visual? GetVisualChild(int index) => index == 0 && _child != null
            ? _child : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size available) { _child?.Measure(available); return available; }
        protected override Size ArrangeOverride(Size size) { _child?.Arrange(new Rect(size)); return size; }
        protected override void OnRender(DrawingContext context) => context.DrawRectangle(_brush, null, new Rect(RenderSize));
    }

    [Fact]
    public void CssShadow_ShouldPaintCardLayers_WhileAuthoredFilterKeepsOverlayCapture()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();
        try
        {
            const double width = 1480, height = 940;
            var window = CreateWindow(new Grid());
            window.Width = width;
            window.Height = height;
            app.MainWindow = window;
            var shadow = new DropShadowEffect { BlurRadius = 72, ShadowDepth = 28, CssBoxShadowLayer = true };
            var dialog = new ContentDialog { Title = "Dialog", CloseButtonText = "Cancel", Effect = shadow };
            var show = dialog.ShowAsync();
            dialog.Measure(new Size(width, height));
            dialog.Arrange(new Rect(0, 0, width, height));
            var card = Assert.IsType<Border>(dialog.FindName("PART_DialogCard"));
            var host = new MediaRenderCacheHost();
            RecordedDrawing Record()
            {
                var recorder = host.CreateFrameRecorder();
                dialog.Render(recorder);
                return Assert.IsType<RecordedDrawing>(host.FinishRecord(recorder));
            }
            DrawCommand[] Captures(RecordedDrawing drawing) => drawing.Commands.Take(drawing.Count)
                .Where(x => x.Kind == DrawCommandKind.BeginEffectCapture).ToArray();

            var drawing = Record();
            Assert.Empty(Captures(drawing));
            var layers = drawing.Commands.Take(drawing.Count)
                .Where(x => x.Kind == DrawCommandKind.PaintCssShadowLayers).ToArray();
            Assert.Equal(2, layers.Length);
            Assert.All(layers, layer =>
            {
                Assert.Equal(card.ActualWidth, layer.V2, 3);
                Assert.Equal(card.ActualHeight, layer.V3, 3);
            });

            dialog.Effect = new BlurEffect { Radius = 3 };
            var capture = Assert.Single(Captures(Record()));
            Assert.True(capture.V2 >= width && capture.V3 >= height);
            Assert.Null(card.Effect);

            dialog.Effect = shadow;
            Assert.Empty(Captures(Record()));
            dialog.Effect = null;
            Assert.Empty(Captures(Record()));
            dialog.Hide();
            ProcessUiQueue();
            Assert.True(show.IsCompletedSuccessfully);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(480, 360)]
    public void PopupBackdrop_ShouldRenderOnlyInsideCard_AndFollowEffectChanges(double width, double height)
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();
        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;
            var dialog = new ContentDialog { Title = "Dialog", CloseButtonText = "Cancel" };
            var show = dialog.ShowAsync();
            window.Width = width;
            window.Height = height;
            ProcessUiQueue();
            // An unopened native Window has no arranged content root on macOS.
            // Lay out the modal explicitly to exercise real, non-empty draw bounds.
            dialog.Measure(new Size(width, height));
            dialog.Arrange(new Rect(0, 0, width, height));
            var card = Assert.IsType<Border>(dialog.FindName("PART_DialogCard"));
            var sink = new BackdropSink();
            dialog.Render(sink);
            var draw = Assert.Single(sink.Draws);
            Assert.Same(dialog.BackdropEffect, draw.Effect);
            Assert.Equal(card.ActualWidth, draw.Bounds.Width);
            Assert.Equal(card.ActualHeight, draw.Bounds.Height);
            Assert.True(draw.Bounds.Width > 0 && draw.Bounds.Height > 0);
            Assert.True(draw.Bounds.Width < width);

            var replacement = new AcrylicEffect();
            dialog.BackdropEffect = replacement;
            sink.Draws.Clear();
            dialog.Render(sink);
            Assert.Same(replacement, Assert.Single(sink.Draws).Effect);

            dialog.BackdropEffect = null;
            sink.Draws.Clear();
            dialog.Render(sink);
            Assert.Empty(sink.Draws);
            dialog.Hide();
            ProcessUiQueue();
            Assert.True(show.IsCompletedSuccessfully);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    private sealed class BackdropSink : DrawingContextAdapter, IEffectDrawingContext, IOffsetDrawingContext
    {
        public List<(Rect Bounds, IBackdropEffect Effect)> Draws { get; } = new();
        public List<string> Trace { get; } = new();
        public Point Offset { get; set; }
        public bool SupportsCssShadowLayers { get; set; }
        public void BeginEffectCapture(float x, float y, float w, float h) => Trace.Add("capture");
        public void EndEffectCapture() => Trace.Add("end");
        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float captureOriginX = 0, float captureOriginY = 0,
            float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0) => Trace.Add("apply");
        public void PaintCssShadowLayers(IEffect shadows, bool inset, float x, float y, float w, float h,
            float topLeftX, float topLeftY, float topRightX, float topRightY,
            float bottomRightX, float bottomRightY, float bottomLeftX, float bottomLeftY,
            Thickness borderThickness) => Trace.Add(inset ? "inset" : "outer");
        private void Paint(Brush? brush)
        {
            if (ReferenceEquals(brush, Brushes.Blue)) Trace.Add("background");
            if (ReferenceEquals(brush, Brushes.Lime)) Trace.Add("child");
        }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius radius) => Draws.Add((rectangle, effect));
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => Paint(brush);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) => Paint(brush);
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private static Window CreateWindow(UIElement content) => new()
    {
        TitleBarStyle = WindowTitleBarStyle.Native, Width = 800, Height = 600, Content = content
    };
    private static void ProcessUiQueue() => Dispatcher.GetForCurrentThread().ProcessQueue();
    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
    }
    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
        Window.SetKeyStateProviderForTesting(null);
    }
}
