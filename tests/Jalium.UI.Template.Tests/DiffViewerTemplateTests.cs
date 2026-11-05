using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DiffViewerTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplateSeparatesDocumentPixelsFromFrameAndViewport()
    {
        var viewer = Create();
        Layout(viewer);
        Assert.Same(viewer, Part<DiffContentView>(viewer, "PART_ContentView").Owner);
        Assert.NotNull(viewer.DiffContentClip);
        Assert.Same(viewer.Background, Part<Border>(viewer, "RootBorder").Background);
        Assert.Equal(Visibility.Visible, viewer.MinimapVisibility);
        Assert.Equal(viewer.MinimapBounds.Width, Part<Border>(viewer, "PART_Minimap").ActualWidth);
        Assert.True(viewer.MinimapViewportBounds.Height < viewer.ActualHeight);
        Assert.Equal("SideBySide", VisualStateManager.GetCurrentStateName(viewer, "ViewStates"));
        viewer.ViewMode = DiffViewMode.Unified;
        viewer.IsEnabled = false;
        Layout(viewer);
        Assert.Equal("Unified", VisualStateManager.GetCurrentStateName(viewer, "ViewStates"));
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(viewer, "CommonStates"));
    }

    [Fact]
    public void ScrollingUpdatesViewportAndMinimapFlagHidesItsChrome()
    {
        var viewer = Create();
        Layout(viewer);
        var before = viewer.MinimapViewportBounds;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new KeySource { RootVisual = viewer }, 1, Key.Down)
        {
            RoutedEvent = UIElement.KeyDownEvent, Source = viewer
        };
        typeof(DiffViewer).GetMethod("OnKeyDownHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewer, [viewer, args]);
        Layout(viewer);
        Assert.True(args.Handled);
        Assert.True(viewer.MinimapViewportBounds.Y > before.Y);
        Assert.True(viewer.MinimapViewportBounds.Bottom <= viewer.ActualHeight);
        viewer.ShowMinimap = false;
        Layout(viewer);
        Assert.Equal(Visibility.Collapsed, Part<Border>(viewer, "PART_MinimapViewport").Visibility);
    }

    [Fact]
    public void ReplacingTemplateReleasesTheSurfaceAndPreservesDiffState()
    {
        var viewer = Create();
        Layout(viewer);
        var retired = Part<DiffContentView>(viewer, "PART_ContentView");
        var text = viewer.ModifiedText;
        var template = new ControlTemplate(typeof(DiffViewer));
        template.SetVisualTree(() => new Border());
        viewer.Template = template;
        Layout(viewer);
        Assert.Null(retired.Owner);
        Assert.Equal(text, viewer.ModifiedText);
        viewer.ClearValue(Control.TemplateProperty);
        Layout(viewer);
        Assert.Same(viewer, Part<DiffContentView>(viewer, "PART_ContentView").Owner);
        Assert.NotSame(retired, viewer.FindName("PART_ContentView"));
        Layout(new DiffContentView());
    }

    [Fact]
    public void WordHighlightingPreservesSpacesBetweenChangedTokens()
    {
        var viewer = Create();
        Layout(viewer);
        var ink = new Ink();
        var tokens = new List<WordDiff> { new(DiffLineType.Unchanged, "alpha  "), new(DiffLineType.Added, "beta") };
        typeof(DiffViewer).GetMethod("RenderWordDiffs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewer, [ink, tokens, 0.0, 0.0, "Menlo", 13.0, false]);
        Assert.Equal(2, ink.Labels.Count);
        Assert.True(ink.Labels[1].X > ink.Labels[0].Width + 2);
    }

    private static DiffViewer Create()
    {
        var viewer = new DiffViewer
        {
            OriginalText = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"original {i}")),
            ModifiedText = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"modified {i}"))
        };
        viewer.Resources.MergedDictionaries.Add(Defaults.Value);
        return viewer;
    }

    private static T Part<T>(Control owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private sealed class KeySource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private static void Layout(FrameworkElement element)
    {
        for (var i = 0; i < 4; i++)
        {
            element.InvalidateMeasure();
            element.Measure(new Size(360, 240));
            element.Arrange(new Rect(0, 0, 360, 240));
            element.UpdateLayout();
        }
    }
    private sealed class Ink : DrawingContextAdapter
    {
        internal readonly List<string> Text = new();
        internal readonly List<(string Text, double X, double Width)> Labels = new();
        public override void DrawText(FormattedText text, Point origin)
        {
            Text.Add(text.Text);
            Labels.Add((text.Text, origin.X, text.Width));
        }
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
