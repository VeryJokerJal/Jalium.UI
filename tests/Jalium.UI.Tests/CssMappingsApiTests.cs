using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>Public mapping registry: aliases, custom converters, multi-property converters, value-parsing helpers.</summary>
public sealed class CssMappingsApiTests
{
    static CssMappingsApiTests()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);
    }

    private static string UniqueName(string prefix)
        => prefix + "-" + Guid.NewGuid().ToString("N")[..6];

    [Fact]
    public void Alias_RedirectsToBuiltInProperty()
    {
        CssMappings.RegisterAlias("brand-radius-x", "border-radius");
        var border = new Border();
        Css.SetStyle(border, "brand-radius-x: 8px");
        Assert.Equal(new CornerRadius(8), border.CornerRadius);
    }

    [Fact]
    public void Alias_ChainsAndMergesWithTargetLonghand()
    {
        CssMappings.RegisterAlias("token-a-radius", "token-b-radius");
        CssMappings.RegisterAlias("token-b-radius", "border-radius");
        var border = new Border();

        // Alias and target compete as the same longhand: the later declaration wins.
        Css.SetStyle(border, "token-a-radius: 4px; border-radius: 9px");
        Assert.Equal(new CornerRadius(9), border.CornerRadius);
    }

    [Fact]
    public void Alias_CycleThrows()
    {
        CssMappings.RegisterAlias("cycle-a", "cycle-b");
        CssMappings.RegisterAlias("cycle-b", "cycle-c");
        Assert.Throws<ArgumentException>(() => CssMappings.RegisterAlias("cycle-c", "cycle-a"));
    }

    [Fact]
    public void Converter_FixedDp_ConvertsAndDropsInvalid()
    {
        CssMappings.RegisterProperty("glow-level", UIElement.OpacityProperty, new TenthsOpacityConverter());

        var border = new Border();
        Css.SetStyle(border, "glow-level: 5");
        Assert.Equal(0.5, border.Opacity);

        // Invalid value: declaration dropped, others still apply, no exception.
        Css.SetStyle(border, "glow-level: chaos; width: 40px");
        Assert.Equal(1.0, border.Opacity);
        Assert.Equal(40.0, border.Width);
    }

    [Fact]
    public void Converter_ByName_ResolvesPerElementType()
    {
        CssMappings.RegisterProperty("surface-tint", "Background", new ColorToBrushConverter());

        var border = new Border();
        Css.SetStyle(border, "surface-tint: rgb(0 128 255)");
        var brush = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(Color.FromArgb(0xFF, 0, 128, 255), brush.Color);
    }

    [Fact]
    public void Converter_OverridesUnsupportedPlaceholder()
    {
        // letter-spacing is a built-in Unsupported placeholder; a user registration wins.
        CssMappings.RegisterProperty("letter-spacing", UIElement.OpacityProperty, new LengthToOpacityConverter());

        var border = new Border();
        Css.SetStyle(border, "letter-spacing: 5px");
        Assert.Equal(0.5, border.Opacity);
    }

    [Fact]
    public void CacheByValueFalse_InvokesPerApplication()
    {
        var converter = new CountingConverter();
        CssMappings.RegisterProperty(
            "counting-prop", UIElement.OpacityProperty, converter, cacheByValue: false);

        var a = new Border();
        var b = new Border();
        Css.SetStyle(a, "counting-prop: x");
        Css.SetStyle(b, "counting-prop: x");
        Assert.True(converter.Calls >= 2);
    }

    [Fact]
    public void Converter_FixedDpCached_ReceivesPropertyTypeAndInvariantCulture()
    {
        var converter = new RecordingConverter { Result = 0.5 };
        var name = UniqueName("record-fixed");
        CssMappings.RegisterProperty(name, UIElement.OpacityProperty, converter);

        var border = new Border();
        Css.SetStyle(border, $"{name}: anything");

        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(typeof(double), converter.TargetType);
        Assert.Equal(CultureInfo.InvariantCulture, converter.Culture);
    }

    [Fact]
    public void Converter_FixedDpLazy_ReceivesPropertyType()
    {
        var converter = new RecordingConverter { Result = 0.25 };
        var name = UniqueName("record-lazy");
        CssMappings.RegisterProperty(name, UIElement.OpacityProperty, converter, cacheByValue: false);

        var border = new Border();
        Css.SetStyle(border, $"{name}: anything");

        Assert.Equal(0.25, border.Opacity);
        Assert.Equal(typeof(double), converter.TargetType);
        Assert.Equal(CultureInfo.InvariantCulture, converter.Culture);
    }

    [Fact]
    public void Converter_ByNameCached_ReceivesObjectTargetType()
    {
        // A cached by-name conversion happens at compile time, before any element exists,
        // so the shared result cannot depend on the eventual owner's property type.
        var brush = new SolidColorBrush(Color.FromArgb(0xFF, 1, 2, 3));
        brush.Freeze();
        var converter = new RecordingConverter { Result = brush };
        var name = UniqueName("record-byname");
        CssMappings.RegisterProperty(name, "Background", converter);

        var border = new Border();
        Css.SetStyle(border, $"{name}: anything");

        Assert.Same(brush, border.Background);
        Assert.Equal(typeof(object), converter.TargetType);
    }

    [Fact]
    public void Converter_ByNameLazy_ReceivesResolvedPropertyType()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0xFF, 4, 5, 6));
        brush.Freeze();
        var converter = new RecordingConverter { Result = brush };
        var name = UniqueName("record-byname-lazy");
        CssMappings.RegisterProperty(name, "Background", converter, cacheByValue: false);

        var border = new Border();
        Css.SetStyle(border, $"{name}: anything");

        Assert.Same(brush, border.Background);
        Assert.Equal(typeof(Brush), converter.TargetType);
    }

    [Fact]
    public void Converter_ReceivesRegistrationParameter()
    {
        var converter = new RecordingConverter { Result = 0.5 };
        var name = UniqueName("record-param");
        CssMappings.RegisterProperty(name, UIElement.OpacityProperty, converter, converterParameter: 4.0);

        var border = new Border();
        Css.SetStyle(border, $"{name}: anything");

        Assert.Equal(4.0, converter.Parameter);
    }

    [Fact]
    public void Converter_ThrowingIsTreatedAsInvalid()
    {
        var name = UniqueName("throwing");
        CssMappings.RegisterProperty(name, UIElement.OpacityProperty, new ThrowingConverter());

        var border = new Border();
        Css.SetStyle(border, $"{name}: boom; width: 40px");

        // The throwing declaration is dropped; the rest of the block still applies.
        Assert.Equal(1.0, border.Opacity);
        Assert.Equal(40.0, border.Width);
    }

    [Fact]
    public void MultiPropertyConverter_ProducesMultipleAssignments()
    {
        CssMappings.RegisterProperty("card-size", new TwoPropertyConverter());
        var border = new Border();
        Css.SetStyle(border, "card-size: 120px");
        Assert.Equal(120.0, border.Width);
        Assert.Equal(60.0, border.Height);
    }

    [Fact]
    public void MultiPropertyConverter_NullDropsDeclarationAndReceivesContext()
    {
        var converter = new RecordingMultiConverter { Result = null };
        var name = UniqueName("multi-null");
        CssMappings.RegisterProperty(name, converter, converterParameter: "ctx");

        var border = new Border();
        Css.SetStyle(border, $"{name}: whatever; width: 40px");

        Assert.Equal(40.0, border.Width);
        Assert.True(double.IsNaN(border.Height));
        Assert.Same(border, converter.Element);
        Assert.Equal("ctx", converter.Parameter);
        Assert.Equal(CultureInfo.InvariantCulture, converter.Culture);
    }

    [Fact]
    public void MultiPropertyConverter_EmptyResultIsSuccessfulNoOp()
    {
        var converter = new RecordingMultiConverter { Result = Array.Empty<CssPropertyAssignment>() };
        var name = UniqueName("multi-empty");
        CssMappings.RegisterProperty(name, converter);

        var border = new Border();
        Css.SetStyle(border, $"{name}: whatever; width: 40px");

        Assert.Equal(40.0, border.Width);
        Assert.True(double.IsNaN(border.Height));
    }

    [Fact]
    public void LateRegistration_RefreshesExistingSheets()
    {
        var border = new Border();
        var panel = new StackPanel();
        panel.Children.Add(border);
        var unique = UniqueName("late-prop");
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse($"Border {{ {unique}: 9 }}"));
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        Assert.Equal(1.0, border.Opacity);

        CssMappings.RegisterProperty(unique, UIElement.OpacityProperty, new TenthsOpacityConverter());
        // Production re-evaluation flows through the Application root invalidator; headless
        // tests drive the subtree refresh manually (the recompiled rules are picked up via
        // the registry-version check).
        CssEvaluationScheduler.InvalidateSubtree(panel);
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        Assert.Equal(0.9, border.Opacity);
    }

    [Fact]
    public void LateRegistration_RecompilesInlineStyles()
    {
        var border = new Border();
        var unique = UniqueName("late-inline");
        Css.SetStyle(border, $"{unique}: 7");
        Assert.Equal(1.0, border.Opacity);

        // The inline block is compiled eagerly and held by the element, so a registration
        // arriving afterwards has to force a recompile rather than only clearing the cache.
        CssMappings.RegisterProperty(unique, UIElement.OpacityProperty, new TenthsOpacityConverter());
        CssEvaluationScheduler.InvalidateElement(border);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);

        Assert.Equal(0.7, border.Opacity);
    }

    [Fact]
    public void LateAliasRegistration_RecompilesInlineStyles()
    {
        var border = new Border();
        var unique = UniqueName("late-alias");
        Css.SetStyle(border, $"{unique}: 12px");
        Assert.Equal(new CornerRadius(0), border.CornerRadius);

        // An alias resolves at compile time, so it needs the same recompile path.
        CssMappings.RegisterAlias(unique, "border-radius");
        CssEvaluationScheduler.InvalidateElement(border);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);

        Assert.Equal(new CornerRadius(12), border.CornerRadius);
    }

    [Theory]
    [InlineData("#ff0000", true, 0xFF, 0x00, 0x00)]
    [InlineData("rebeccapurple", true, 0x66, 0x33, 0x99)]
    [InlineData("rgb(1 2 3)", true, 1, 2, 3)]
    [InlineData("currentcolor", false, 0, 0, 0)]
    [InlineData("notacolor", false, 0, 0, 0)]
    public void ValueParsing_Color(string text, bool expected, int r, int g, int b)
    {
        Assert.Equal(expected, CssValueParsing.TryParseColor(text, out var color));
        if (expected)
        {
            Assert.Equal(Color.FromArgb(0xFF, (byte)r, (byte)g, (byte)b), color);
        }
    }

    [Theory]
    [InlineData("12px", true, 12.0)]
    [InlineData("1in", true, 96.0)]
    [InlineData("1em", false, 0.0)]
    [InlineData("50%", false, 0.0)]
    public void ValueParsing_Length(string text, bool expected, double px)
    {
        Assert.Equal(expected, CssValueParsing.TryParseLength(text, out var value));
        if (expected)
        {
            Assert.Equal(px, value);
        }
    }

    // ── Converter classes ──────────────────────────────────────────────────────────────

    /// <summary>"5" → 0.5, clamped; anything unparsable drops the declaration.</summary>
    private sealed class TenthsOpacityConverter : ICssValueConverter
    {
        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
            => double.TryParse(rawValue, NumberStyles.Float, culture, out var v)
                ? Math.Clamp(v / 10.0, 0, 1)
                : null;
    }

    private sealed class LengthToOpacityConverter : ICssValueConverter
    {
        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
            => CssValueParsing.TryParseLength(rawValue, out var px) ? Math.Clamp(px / 10, 0, 1) : null;
    }

    private sealed class ColorToBrushConverter : ICssValueConverter
    {
        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!CssValueParsing.TryParseColor(rawValue, out var color))
            {
                return null;
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    private sealed class CountingConverter : ICssValueConverter
    {
        public int Calls;

        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
        {
            Calls++;
            return 0.5;
        }
    }

    private sealed class RecordingConverter : ICssValueConverter
    {
        public object? Result;
        public Type? TargetType;
        public object? Parameter;
        public CultureInfo? Culture;

        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
        {
            TargetType = targetType;
            Parameter = parameter;
            Culture = culture;
            return Result;
        }
    }

    private sealed class ThrowingConverter : ICssValueConverter
    {
        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
            => throw new InvalidOperationException("converter failure must not escape the engine");
    }

    /// <summary>One length in, both size properties out.</summary>
    private sealed class TwoPropertyConverter : ICssMultiPropertyConverter
    {
        public IReadOnlyList<CssPropertyAssignment>? Convert(
            string rawValue, FrameworkElement element, object? parameter, CultureInfo culture)
        {
            if (!CssValueParsing.TryParseLength(rawValue, out var px))
            {
                return null;
            }

            return new[]
            {
                new CssPropertyAssignment(FrameworkElement.WidthProperty, px),
                new CssPropertyAssignment(FrameworkElement.HeightProperty, px / 2),
            };
        }
    }

    private sealed class RecordingMultiConverter : ICssMultiPropertyConverter
    {
        public IReadOnlyList<CssPropertyAssignment>? Result;
        public FrameworkElement? Element;
        public object? Parameter;
        public CultureInfo? Culture;

        public IReadOnlyList<CssPropertyAssignment>? Convert(
            string rawValue, FrameworkElement element, object? parameter, CultureInfo culture)
        {
            Element = element;
            Parameter = parameter;
            Culture = culture;
            return Result;
        }
    }
}
