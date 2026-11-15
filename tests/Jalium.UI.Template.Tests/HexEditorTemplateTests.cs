using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class HexEditorTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplateOwnsTheFrameViewAndScrollRange()
    {
        var editor = Create(); Layout(editor);
        Assert.IsType<Border>(editor.GetVisualChild(0));
        var view = Assert.IsType<HexEditorTextView>(editor.FindName("PART_TextView"));
        var bar = Assert.IsType<ScrollBar>(editor.FindName("PART_VerticalScrollBar"));
        Assert.Equal(new Point(7, 7), view.TransformToAncestor(editor));
        Assert.Equal(new Size(372, 186), view.RenderSize);
        Assert.True(bar.Maximum > 0); Assert.True(bar.ViewportSize > 0);
        Assert.False(bar.Focusable); Assert.False(bar.IsTabStop);
    }

    [Fact]
    public void CssInsetsAndRuntimeBarWidthAllocateTheActualTextViewport()
    {
        var editor = Create(); Layout(editor);
        var bar = Assert.IsType<ScrollBar>(editor.FindName("PART_VerticalScrollBar"));
        bar.Width = 28;
        Css.SetStyle(editor, "border:3px solid lime;padding:12px");
        Layout(editor);
        var view = Assert.IsType<HexEditorTextView>(editor.FindName("PART_TextView"));
        Assert.Equal(new Point(15, 15), view.TransformToAncestor(editor));
        Assert.Equal(new Size(342, 170), view.RenderSize);
        Assert.Equal(28, bar.RenderSize.Width);
    }

    [Fact]
    public void RetiredPartsCannotScrollTheOwnerAndCurrentBarKeepsItsBinding()
    {
        var editor = Create(); var source = new ProgressBar();
        ProbeBar? old = null; HexEditorTextView? oldView = null;
        var template = new ControlTemplate(typeof(HexEditor));
        template.SetVisualTree(() =>
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            oldView = new HexEditorTextView { Name = "PART_TextView" };
            old = new ProbeBar { Name = "PART_VerticalScrollBar", Width = 14 };
            BindingOperations.SetBinding(old, RangeBase.ValueProperty,
                new Binding(nameof(ProgressBar.Value)) { Source = source, Mode = BindingMode.TwoWay });
            grid.Children.Add(oldView); Grid.SetColumn(old, 1); grid.Children.Add(old); return grid;
        });
        editor.Template = template; Layout(editor);
        old!.PressDown(); Assert.True(Offset(editor) > 0);
        Assert.NotNull(BindingOperations.GetBindingExpression(old, RangeBase.ValueProperty));
        var data = editor.Data; var offset = Offset(editor);
        var blank = new ControlTemplate(typeof(HexEditor)); blank.SetVisualTree(() => new Border { Height = 20 });
        editor.Template = blank; Layout(editor);
        old.PressDown(); Assert.Equal(offset, Offset(editor));
        oldView!.InvalidateMeasure(); oldView.Measure(new Size(200, 100));
        Assert.Equal(default(Size), oldView.DesiredSize);
        editor.ClearValue(Control.TemplateProperty); Layout(editor);
        Assert.Same(data, editor.Data);
        Assert.NotSame(old, editor.FindName("PART_VerticalScrollBar"));
        Assert.Equal(offset, Offset(editor));
    }

    [Fact]
    public void TemplatedViewRendersLiveBytesInsteadOfAnOpaqueEmptyFrame()
    {
        var editor = Create(); editor.Data = [0x41, 0x42, 0x43, 0x44]; Layout(editor);
        var before = new Ink(); editor.Render(before);
        Assert.Contains(before.Text, text => text.Contains("41 42 43 44", StringComparison.Ordinal));
        editor.Data = [0x55, 0x66]; Layout(editor);
        var after = new Ink(); editor.Render(after);
        Assert.Contains(after.Text, text => text.Contains("55 66", StringComparison.Ordinal));
        Assert.DoesNotContain(after.Text, text => text.Contains("41 42 43 44", StringComparison.Ordinal));
    }

    [Fact]
    public void ScopedPaletteResourcesStayLiveAndLocalBrushesTakePriority()
    {
        var editor = Create();
        var scoped = new SolidColorBrush(Color.FromRgb(0, 94, 166));
        editor.Resources["HexEditorHexForeground"] = scoped;
        Layout(editor);
        Assert.Same(scoped, editor.HexForeground);
        var replacement = new SolidColorBrush(Color.FromRgb(143, 78, 50));
        editor.Resources["HexEditorHexForeground"] = replacement;
        Layout(editor);
        Assert.Same(replacement, editor.HexForeground);
        editor.HexForeground = Brushes.Red;
        editor.Resources["HexEditorHexForeground"] = Brushes.Blue;
        Layout(editor);
        Assert.Same(Brushes.Red, editor.HexForeground);
    }

    private static HexEditor Create()
    {
        var editor = new HexEditor { Data = Enumerable.Range(0, 8192).Select(value => (byte)value).ToArray() };
        editor.Resources.MergedDictionaries.Add(Defaults.Value); return editor;
    }
    private static long Offset(HexEditor editor) => (long)typeof(HexEditor).GetField("_scrollOffset", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(400, 200));
        element.Arrange(new Rect(0, 0, 400, 200)); element.UpdateLayout();
    }
    private sealed class ProbeBar : ScrollBar
    {
        public void PressDown() => OnKeyDown(new KeyEventArgs(Keyboard.PrimaryDevice,
            new Source { RootVisual = this }, Environment.TickCount, Key.Down));
    }
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        protected override CompositionTarget? GetCompositionTargetCore() => null;
        public override bool IsDisposed => false;
    }
    private sealed class Ink : DrawingContextAdapter
    {
        public List<string> Text { get; } = [];
        public override void DrawText(FormattedText text, Point point) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
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
