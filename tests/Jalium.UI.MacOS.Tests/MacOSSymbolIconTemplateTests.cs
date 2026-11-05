using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSSymbolIconTemplateTests
{
    public MacOSSymbolIconTemplateTests() => ThemeManager.RegisterControlThemes(new ResourceDictionary { [typeof(SymbolIcon)] = MenuFlyoutTemplateTestHelper.Themes.Value[typeof(SymbolIcon)] });
    [Fact]
    public void EveryDeclaredSymbolHasAFrozenFiniteOutlineAndAliasesShareTheirCodepoint()
    {
        var geometries = new Dictionary<int, Geometry>();
        foreach (var symbol in Enum.GetValues<Symbol>())
        {
            var icon = new SymbolIcon(symbol);
            var geometry = Assert.IsAssignableFrom<Geometry>(icon.Geometry);
            Assert.True(geometry.IsFrozen, symbol.ToString());
            var bounds = geometry.Bounds;
            Assert.True(double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width > 0 && bounds.Height > 0, $"{symbol}: {bounds}");
            if (geometries.TryGetValue((int)symbol, out var prior))
                Assert.Same(prior, geometry);
            geometries[(int)symbol] = geometry;
        }

        Assert.Equal(729, geometries.Count);
    }

    [Fact]
    public void DefaultTemplateUsesTheSemanticOutlineAndPreservesInheritedForeground()
    {
        var icon = new SymbolIcon(Symbol.Print);
        var parent = new ContentControl
        {
            Content = icon,
            Foreground = Brushes.Blue
        };
        Layout(parent);
        Layout(icon);
        Assert.IsAssignableFrom<Control>(icon);
        var path = Assert.IsType<PathIcon>(icon.FindName("PART_Vector"));
        var glyph = Assert.IsType<FontIcon>(icon.FindName("PART_Glyph"));
        Assert.Same(icon.Geometry, path.Data);
        Assert.Equal(new Rect(0, 0, 2048, 2048), path.ViewBox);
        Assert.Same(Brushes.Blue, parent.Foreground);
        Assert.Same(parent.Foreground, icon.Foreground);
        Assert.Same(icon.Foreground, path.Foreground);
        Assert.Equal(icon.UsesGlyph ? "Glyph" : "Vector", VisualStateManager.GetCurrentStateName(icon, "SymbolStates"));
        Assert.Equal(icon.UsesGlyph ? Visibility.Collapsed : Visibility.Visible, path.Visibility);
        parent.Foreground = Brushes.Green;
        Assert.Same(Brushes.Green, RenderedBrush(path));
        icon.Foreground = Brushes.Red;
        parent.Foreground = Brushes.Black;
        Assert.Same(Brushes.Red, RenderedBrush(path));
        Assert.Same(Brushes.Red, glyph.Foreground);
        icon.ClearValue(IconElement.ForegroundProperty);
        Assert.Same(Brushes.Black, RenderedBrush(path));
        GC.KeepAlive(parent);
    }

    [Fact]
    public void ScopedThemeBrushUpdatesWithoutOverridingAnInheritedOrLocalForeground()
    {
        var icon = new SymbolIcon(Symbol.Save);
        icon.Resources["TextPrimary"] = Brushes.Blue;
        Layout(icon);
        var path = Assert.IsType<PathIcon>(icon.FindName("PART_Vector"));
        Assert.Same(Brushes.Blue, RenderedBrush(path));
        icon.Resources["TextPrimary"] = Brushes.Green;
        Assert.Same(Brushes.Green, RenderedBrush(path));
        icon.Foreground = Brushes.Red;
        icon.Resources["TextPrimary"] = Brushes.Black;
        Assert.Same(Brushes.Red, RenderedBrush(path));
    }

    [Fact]
    public void ReplacementTemplatesOwnAllVisualsAndOldBindingsRetire()
    {
        var icon = new SymbolIcon(Symbol.ZoomIn);
        Layout(icon);
        var old = Assert.IsType<PathIcon>(icon.FindName("PART_Vector"));
        var oldData = old.Data;
        var blank = new ControlTemplate(typeof(SymbolIcon));
        blank.SetVisualTree(() => new Border { Background = Brushes.Blue });
        icon.Template = blank;
        Layout(icon);
        icon.Symbol = Symbol.Print;
        Assert.NotSame(oldData, icon.Geometry);
        Assert.Null(old.Data);
        Assert.Null(icon.FindName("PART_Vector"));
        icon.ClearValue(Control.TemplateProperty);
        Layout(icon);
        var current = Assert.IsType<PathIcon>(icon.FindName("PART_Vector"));
        Assert.NotSame(old, current);
        Assert.Same(icon.Geometry, current.Data);
        icon.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(icon, "CommonStates"));
        icon.IsEnabled = true;
        Assert.Equal("Normal", VisualStateManager.GetCurrentStateName(icon, "CommonStates"));
    }

    [Fact]
    public void InvalidCodepointsDoNotThrowOrLeavePreviousOutlinesVisible()
    {
        var icon = new SymbolIcon(Symbol.Save);
        Layout(icon);
        foreach (int code in new[]
        {
            -1,
            0xD800,
            0x110000
        }

        )
        {
            icon.Symbol = (Symbol)code;
            Assert.Null(icon.Geometry);
            Assert.Equal(string.Empty, icon.Glyph);
            Assert.False(icon.UsesGlyph);
            Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(icon, "SymbolStates"));
        }

        icon.Symbol = Symbol.Cancel;
        Assert.NotNull(icon.Geometry);
    }

    [Fact]
    public void SharedRendererResolvesEnumElementAndStringThroughTheSameOutline()
    {
        if (SymbolIcon.HasNativeSymbolFont)
            return;
        var symbol = new SymbolIcon(Symbol.Print)
        {
            Foreground = Brushes.Blue
        };
        foreach (object source in new object[]
        {
            Symbol.Print,
            symbol,
            "Symbol.Print",
            new SymbolIconSource
            {
                Symbol = Symbol.Print
            }
        }

        )
        {
            var context = new IconContext();
            IconRendering.DrawIcon(context, source, new Rect(0, 0, 40, 20), Brushes.Blue);
            Assert.Same(symbol.Geometry, Assert.Single(context.Geometries));
            Assert.Same(Brushes.Blue, Assert.Single(context.Brushes));
            Assert.Empty(context.Text);
            Assert.Single(context.Transforms);
        }

        var invalid = new IconContext();
        IconRendering.DrawIcon(invalid, (Symbol)(-1), new Rect(0, 0, 20, 20), Brushes.Blue);
        Assert.Empty(invalid.Geometries);
        Assert.Empty(invalid.Text);
    }

    [Fact]
    public void SymbolChangesKeepCallerBindingsAndNativeTextOptionsReachTheGlyphPart()
    {
        var source = new SymbolIcon(Symbol.Save);
        var icon = new SymbolIcon();
        BindingOperations.SetBinding(icon, SymbolIcon.SymbolProperty, new Binding(nameof(SymbolIcon.Symbol)) { Source = source });
        TextOptions.SetTextRenderingMode(icon, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(icon, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(icon, TextHintingMode.Animated);
        Layout(icon);
        var path = Assert.IsType<PathIcon>(icon.FindName("PART_Vector"));
        var glyph = Assert.IsType<FontIcon>(icon.FindName("PART_Glyph"));
        source.Symbol = Symbol.Print;
        Assert.Equal(Symbol.Print, icon.Symbol);
        Assert.Same(icon.Geometry, path.Data);
        Assert.NotNull(BindingOperations.GetBindingExpression(icon, SymbolIcon.SymbolProperty));
        Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(glyph));
        Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(glyph));
        Assert.Equal(TextHintingMode.Animated, TextOptions.GetTextHintingMode(glyph));
    }

    [Fact]
    public void RetiredUnloadedTemplatePartsAreNotRetainedByForegroundObservers()
    {
        var(icon, oldPart) = RetirePart();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(oldPart.IsAlive);
        GC.KeepAlive(icon);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (SymbolIcon, WeakReference) RetirePart()
    {
        var icon = new SymbolIcon(Symbol.Save);
        Layout(icon);
        var oldPart = new WeakReference(icon.FindName("PART_Vector"));
        var template = new ControlTemplate(typeof(SymbolIcon));
        template.SetVisualTree(() => new Border());
        icon.Template = template;
        Layout(icon);
        return (icon, oldPart);
    }

    private static Brush? RenderedBrush(PathIcon icon)
    {
        icon.Visibility = Visibility.Visible;
        var context = new IconContext();
        icon.Render(context);
        return Assert.Single(context.Brushes);
    }

    private static void Layout(FrameworkElement icon)
    {
        icon.Measure(new Size(40, 40));
        icon.Arrange(new Rect(0, 0, 40, 40));
    }

    private sealed class IconContext : DrawingContextAdapter
    {
        public List<Geometry> Geometries { get; } = [];
        public List<Brush?> Brushes { get; } = [];
        public List<FormattedText> Text { get; } = [];
        public List<Transform> Transforms { get; } = [];

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            Geometries.Add(geometry);
            Brushes.Add(brush);
        }

        public override void DrawText(FormattedText text, Point origin) => Text.Add(text);
        public override void PushTransform(Transform transform) => Transforms.Add(transform);
        public override void DrawLine(Pen pen, Point p0, Point p1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double rx, double ry)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double rx, double ry)
        {
        }

        public override void DrawImage(ImageSource image, Rect rect)
        {
        }

        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius)
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
