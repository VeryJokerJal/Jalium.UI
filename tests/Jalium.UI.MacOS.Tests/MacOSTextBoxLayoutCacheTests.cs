#if JALIUM_MAC_NATIVE_GEOMETRY_TESTS
using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTextBoxLayoutCacheTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const long PayloadBudget = 512 * 1024;
    private const string Sample = "Miii中文🙂e\u0301 אבג tail";
    private static readonly MethodInfo GetLayout = typeof(TextBox).GetMethod("GetMacOSAlignedLayout", Private)!;
    private static readonly MethodInfo GetWidth = typeof(TextBox).GetMethod("MeasureTextWidth", Private)!;
    private static readonly MethodInfo Unload = typeof(TextBox).GetMethod("OnImeOwnerUnloaded", Private)!;
    private static IDictionary Cache(TextBox box) => (IDictionary)typeof(TextBox).GetField("_macOSAlignedLayouts", Private)!.GetValue(box)!;
    private static IDictionary Widths(TextBox box) => (IDictionary)typeof(TextBox).GetField("_textWidthCache", Private)!.GetValue(box)!;
    private static string KeyText(object key) => (string)key.GetType().GetProperty("Text")!.GetValue(key)!;
    private static long LayoutBytes(TextBox box) => Cache(box).Keys.Cast<object>().Sum(key => 2L * KeyText(key).Length);
    private static long WidthBytes(TextBox box) => Widths(box).Keys.Cast<string>().Sum(text => 2L * text.Length);
    private static object Layout(TextBox box, string text, double width = 360) => GetLayout.Invoke(box, [text, width])!;
    private static double Width(TextBox box, string text) => (double)GetWidth.Invoke(box, [text])!;
    private static NativeTextFormat Format(object layout) => Assert.IsType<NativeTextFormat>(layout.GetType().GetProperty("Format", Private)!.GetValue(layout));

    [Theory, InlineData(false), InlineData(true)]
    public void EditingRetiresPreviousTextAndNativeFormatsImmediately(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box;
        for (int revision = 0; revision < 5; revision++)
        {
            string text = Sample + new string('i', 8192) + revision;
            box.Text = text; var previous = Format(Layout(box, text)); Assert.True(Width(box, text) > 0);
            Assert.True(previous.IsValid); box.Text = text + "changed";
            Assert.Equal(nint.Zero, previous.Handle); Assert.Empty(Cache(box)); Assert.Empty(Widths(box));
        }
    }

    [Theory, InlineData(false), InlineData(true)]
    public void MultipleCurrentRowsHaveABoundedTextPayload(bool software)
    {
        string[] rows = Enumerable.Range(0, 6).Select(i => new string((char)('a' + i), 70 * 1024)).ToArray();
        using var scope = new Owner(software, string.Join('\n', rows)); var box = scope.Box;
        var original = Format(Layout(box, rows[0]));
        foreach (string row in rows.Skip(1)) Layout(box, row);
        Assert.InRange(LayoutBytes(box), 1, PayloadBudget); Assert.Equal(nint.Zero, original.Handle);
        Assert.All(Cache(box).Values.Cast<object>(), value => Assert.True(Format(value).IsValid));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void WidthQueriesAlsoHaveABoundedTextPayload(bool software)
    {
        string[] rows = Enumerable.Range(0, 6).Select(i => new string((char)('a' + i), 70 * 1024)).ToArray();
        using var scope = new Owner(software, string.Join('\n', rows));
        foreach (string row in rows) Assert.True(Width(scope.Box, row) > 0);
        Assert.InRange(WidthBytes(scope.Box), 1, PayloadBudget);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AnOversizedCurrentLineRetainsOnlyOneNativeLayout(bool software)
    {
        string text = new('i', 300 * 1024); using var scope = new Owner(software, text); var box = scope.Box;
        NativeTextFormat? previous = null;
        foreach (double width in new[] { 240d, 320d, 400d })
        {
            var current = Format(Layout(box, text, width)); Assert.True(current.IsValid);
            Assert.Single(Cache(box)); if (previous is not null) Assert.Equal(nint.Zero, previous.Handle); previous = current;
        }
        double first = Width(box, text), second = Width(box, text);
        Assert.True(double.IsFinite(first) && first > 0); Assert.Equal(first, second); Assert.Empty(Widths(box));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void UnchangedTextAndViewportReuseTheLiveLayout(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box; var first = Layout(box, box.Text);
        double width = Width(box, box.Text); box.Text = new string(box.Text.ToCharArray());
        Assert.Same(first, Layout(box, box.Text)); Assert.True(Format(first).IsValid);
        Assert.Equal(width, Width(box, box.Text)); Assert.Single(Cache(box)); Assert.Single(Widths(box));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void UnloadingRetiresBothOwnerCaches(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box;
        var formats = new[] { Format(Layout(box, box.Text, 240)), Format(Layout(box, box.Text, 320)) }; Width(box, box.Text);
        Unload.Invoke(box, [box, new RoutedEventArgs(FrameworkElement.UnloadedEvent, box)]);
        Assert.Empty(Cache(box)); Assert.Empty(Widths(box)); Assert.All(formats, format => Assert.Equal(nint.Zero, format.Handle));
        Unload.Invoke(box, [box, new RoutedEventArgs(FrameworkElement.UnloadedEvent, box)]); Assert.Empty(Cache(box));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void TextChangedCallbacksSeeRetirementAndKeepTheirNewLayout(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box;
        var previous = Format(Layout(box, box.Text)); Width(box, box.Text); object? current = null; int calls = 0;
        box.TextChanged += (_, _) =>
        {
            calls++; Assert.Equal(nint.Zero, previous.Handle); Assert.Empty(Cache(box)); Assert.Empty(Widths(box));
            current = Layout(box, box.Text); Width(box, box.Text);
        };
        box.Text = "changed 中文🙂"; Assert.Equal(1, calls); Assert.NotNull(current);
        Assert.Same(current, Layout(box, box.Text)); Assert.True(Format(current).IsValid); Assert.Single(Widths(box));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ReturningToOlderTextCreatesAFreshValidLayout(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box;
        var first = Layout(box, Sample); box.Text = "changed " + Sample; var middle = Layout(box, box.Text);
        box.Text = Sample; var current = Layout(box, Sample);
        Assert.NotSame(first, current); Assert.NotSame(middle, current); Assert.True(Format(current).IsValid);
        Assert.Equal(nint.Zero, Format(first).Handle); Assert.Equal(nint.Zero, Format(middle).Handle); Assert.Single(Cache(box));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void UndoAndRedoRetireThePreviousRevision(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box;
        var original = Format(Layout(box, box.Text)); box.Select(0, 4); box.SelectedText = "窄斜体";
        string changed = box.Text; var replacement = Format(Layout(box, changed)); Assert.Equal(nint.Zero, original.Handle);
        Assert.True(box.Undo()); Assert.Equal(Sample, box.Text); Assert.Equal(nint.Zero, replacement.Handle);
        var undone = Format(Layout(box, box.Text)); Assert.True(box.Redo()); Assert.Equal(changed, box.Text);
        Assert.Equal(nint.Zero, undone.Handle); Assert.True(Format(Layout(box, box.Text)).IsValid);
    }

    [Theory, InlineData(0, false), InlineData(1, false), InlineData(2, false), InlineData(0, true), InlineData(1, true), InlineData(2, true)]
    public void EvictionPreservesPaintedCaretAndHitGeometry(int alignment, bool software)
    {
        using var scope = new Owner(software); var box = scope.Box; box.TextAlignment = (TextAlignment)alignment;
        box.Measure(new Size(400, 160)); box.Arrange(new Rect(0, 0, 400, 160));
        int[] offsets = [0, 4, Sample.Length]; Rect[] before = offsets.Select(offset => box.GetRectFromCharacterIndex(offset)).ToArray();
        for (int i = 0; i < 6; i++) Layout(box, new string((char)('a' + i), 70 * 1024), 388);
        foreach (var (offset, expected) in offsets.Zip(before))
        {
            Rect actual = box.GetRectFromCharacterIndex(offset); Assert.Equal(expected, actual);
            Assert.True(double.IsFinite(actual.X) && actual.Height > 0);
            Assert.Equal(offset, box.GetCharacterIndexFromPoint(new Point(actual.X, actual.Y + actual.Height / 2), true));
        }
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AReplacementContextNeverReusesThePreviousFormat(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box; var first = Format(Layout(box, box.Text));
        using var replacement = RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        var current = Format(Layout(box, box.Text)); Assert.NotSame(first, current); Assert.Equal(nint.Zero, first.Handle);
        Assert.True(current.IsValid); Assert.Single(Cache(box));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ANewFontEpochRetiresThePreviousFormat(bool software)
    {
        using var scope = new Owner(software); var box = scope.Box; var first = Format(Layout(box, box.Text));
        TextMeasurement.ClearCache(); var current = Format(Layout(box, box.Text));
        Assert.NotSame(first, current); Assert.Equal(nint.Zero, first.Handle); Assert.True(current.IsValid); Assert.Single(Cache(box));
    }

    private sealed class Owner : IDisposable
    {
        private readonly RenderContext _context;
        internal TextBox Box { get; }
        internal Owner(bool software, string text = Sample)
        {
            Assert.True(OperatingSystem.IsMacOS());
            var backend = software ? RenderBackend.Software : RenderBackend.Metal;
            _context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
            Assert.True(_context.IsValid); Assert.Equal(backend, _context.Backend); Assert.Same(_context, RenderContext.Current);
            Box = new TextBox { Text = text, FontFamily = new FontFamily("Times"), FontSize = 20,
                FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.NoWrap,
                Width = 400, Height = 160, Padding = new Thickness(4), BorderThickness = new Thickness(2) };
            Box.Measure(new Size(400, 160)); Box.Arrange(new Rect(0, 0, 400, 160));
            typeof(TextBox).GetMethod("ClearMacOSAlignedLayouts", Private)!.Invoke(Box, null); Widths(Box).Clear();
        }
        public void Dispose()
        {
            Unload.Invoke(Box, [Box, new RoutedEventArgs(FrameworkElement.UnloadedEvent, Box)]);
            _context.Dispose();
        }
    }
}
#endif
