using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class ColorPickerTemplateTests
{
    [Fact]
    public void HexEditingWithoutAlphaPreservesTheCurrentOpacity()
    {
        var picker = Create();
        picker.Color = Color.FromArgb(64, 10, 20, 30);
        picker.IsAlphaEnabled = false;
        Assert.Equal("#0A141E", picker.HexText);
        Assert.True(picker.TrySetColor("#80112233"));
        Assert.Equal(Color.FromArgb(64, 17, 34, 51), picker.Color);
        Assert.Equal("#112233", picker.HexText);
        picker.HexText = "#445566";
        Assert.True(picker.CommitHexValue());
        Assert.Equal(Color.FromArgb(64, 68, 85, 102), picker.Color);
    }

    [Fact]
    public void CompositionEnterDoesNotCommitTheHexDraft()
    {
        var picker = Create();
        var input = Assert.IsType<TextBox>(picker.FindName("PART_HexTextBox"));
        input.Text = "#123456";
        var internalMethods = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(TextBox).GetMethod("OnImeCompositionStart", internalMethods)!.Invoke(input, null);
        try
        {
            input.RaiseEvent(KeyArgs(input, Key.Enter));
            Assert.Equal(Color.White, picker.Color);
            Assert.False(picker.HasHexError);
        }
        finally
        {
            typeof(TextBox).GetMethod("OnImeCompositionEnd", internalMethods)!.Invoke(input, [string.Empty]);
        }

        input.RaiseEvent(KeyArgs(input, Key.Enter));
        Assert.Equal(Color.FromRgb(18, 52, 86), picker.Color);
    }

    [Fact]
    public void DefaultWidthFitsItsCompositionAndFontSizeFlowsToTheEditablePart()
    {
        var picker = Create();
        Assert.InRange(picker.ActualWidth, 200, 224);
        picker.FontSize = 19;
        var input = Assert.IsType<TextBox>(picker.FindName("PART_HexTextBox"));
        Assert.Equal(19, input.FontSize);
        picker.HexText = "#bad";
        Assert.False(picker.CommitHexValue());
        Assert.Equal(picker.HexErrorText, Jalium.UI.Automation.AutomationProperties.GetHelpText(input));
    }

    [Fact]
    public void HexInputUsesTheOwningPickersExplicitPalette()
    {
        var picker = Create();
        var background = new SolidColorBrush(Color.FromRgb(25, 30, 35));
        picker.Background = background;
        picker.Foreground = Brushes.White;
        picker.ErrorForeground = Brushes.Yellow;
        var input = Assert.IsType<TextBox>(picker.FindName("PART_HexTextBox"));
        Assert.Same(background, input.Background);
        Assert.Same(Brushes.White, input.Foreground);
        Assert.Same(Brushes.Yellow, Assert.IsType<TextBlock>(picker.FindName("PART_HexError")).Foreground);
    }

    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultCompositionUsesReusablePartsAndKeepsHsvForAchromaticColors()
    {
        var picker = Create();
        Assert.IsType<ColorSpectrum>(picker.FindName("PART_Spectrum"));
        Assert.IsType<ColorRamp>(picker.FindName("PART_HueSlider"));
        Assert.IsType<ColorRamp>(picker.FindName("PART_AlphaSlider"));
        Assert.IsType<ColorPreview>(picker.FindName("PART_Preview"));
        Assert.Equal("#FFFFFFFF", Assert.IsType<TextBox>(picker.FindName("PART_HexTextBox")).Text);
        picker.Hue = 240;
        Assert.Equal(Color.White, picker.Color);
        Assert.Equal(240, picker.Hue);
        picker.Saturation = 1;
        Assert.Equal(Color.Blue, picker.Color);
        picker.Value = 0;
        Assert.Equal(Color.Black, picker.Color);
        Assert.Equal(240, picker.Hue);
        picker.Value = 1;
        Assert.Equal(Color.Blue, picker.Color);
    }

    [Fact]
    public void HexDraftCommitsCancelsAndShowsAnErrorWithoutChangingColor()
    {
        var picker = Create();
        var input = Assert.IsType<TextBox>(picker.FindName("PART_HexTextBox"));
        input.Text = "#80112233";
        input.RaiseEvent(KeyArgs(input, Key.Enter));
        Assert.Equal(Color.FromArgb(128, 17, 34, 51), picker.Color);
        Assert.False(picker.HasHexError);
        input.Text = "#bad";
        input.RaiseEvent(KeyArgs(input, Key.Enter));
        Assert.True(picker.HasHexError);
        Assert.Equal(Color.FromArgb(128, 17, 34, 51), picker.Color);
        Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(picker.FindName("PART_HexError")).Visibility);
        input.RaiseEvent(KeyArgs(input, Key.Escape));
        Assert.Equal("#80112233", input.Text);
        Assert.False(picker.HasHexError);
    }

    [Fact]
    public void ComponentHexAndAutomationChangesPreserveTheColorBinding()
    {
        var source = new ColorPicker
        {
            Color = Color.Red
        };
        var picker = Create();
        picker.SetBinding(ColorPicker.ColorProperty, new Binding(nameof(ColorPicker.Color)) { Source = source, Mode = BindingMode.TwoWay });
        Assert.IsType<ColorRamp>(picker.FindName("PART_HueSlider")).Value = 120;
        Assert.Equal(Color.FromRgb(0, 255, 0), source.Color);
        var peer = Assert.IsAssignableFrom<IValueProvider>(picker.GetAutomationPeer());
        peer.SetValue("#80446688");
        Assert.Equal(Color.FromArgb(128, 68, 102, 136), source.Color);
        Assert.NotNull(BindingOperations.GetBindingBase(picker, ColorPicker.ColorProperty));
        Assert.Throws<ArgumentException>(() => peer.SetValue("invalid"));
        picker.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => peer.SetValue("#FFFFFF"));
    }

    [Fact]
    public void MissingPartsAndRetiredHexInputsCannotCommitIntoTheCurrentPicker()
    {
        var picker = Create();
        var input = Assert.IsType<TextBox>(picker.FindName("PART_HexTextBox"));
        var spectrum = Assert.IsType<ColorSpectrum>(picker.FindName("PART_Spectrum"));
        var hue = Assert.IsType<ColorRamp>(picker.FindName("PART_HueSlider"));
        var alpha = Assert.IsType<ColorRamp>(picker.FindName("PART_AlphaSlider"));
        var minimal = new ControlTemplate(typeof(ColorPicker));
        minimal.SetVisualTree(() => new Border());
        picker.Template = minimal;
        Layout(picker);
        input.Text = "#FF123456";
        input.RaiseEvent(KeyArgs(input, Key.Enter));
        hue.Value = 120;
        alpha.Value = 64;
        spectrum.Saturation = 1;
        Assert.Equal(Color.White, picker.Color);
        Assert.True(picker.TrySetColor("#FF112233"));
        Assert.Equal(Color.FromArgb(255, 17, 34, 51), picker.Color);
        picker.IsCompact = true;
        Assert.Equal(120, picker.EffectiveSpectrumSize);
        Assert.Null(picker.FindName("PART_HexTextBox"));
    }

    [Fact]
    public void CompactAndVisibilityOptionsAreTemplateDrivenAndHexCanStandAlone()
    {
        var picker = Create();
        picker.IsCompact = true;
        Layout(picker);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Grid>(picker.FindName("PART_DetailsRow")).Visibility);
        Assert.Equal(14, Assert.IsType<ColorRamp>(picker.FindName("PART_HueSlider")).Height);
        picker.IsCompact = false;
        picker.IsAlphaEnabled = false;
        picker.IsColorPreviewVisible = false;
        Layout(picker);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ColorRamp>(picker.FindName("PART_AlphaSlider")).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<TextBox>(picker.FindName("PART_HexTextBox")).Visibility);
        Assert.True(picker.ShowHexInput);
        Assert.False(picker.ShowPreview);
    }

    private static ColorPicker Create()
    {
        ThemeManager.RegisterControlThemes(new ResourceDictionary { [typeof(ColorSpectrum)] = Defaults.Value[typeof(ColorSpectrum)], [typeof(ColorRamp)] = Defaults.Value[typeof(ColorRamp)], [typeof(ColorPreview)] = Defaults.Value[typeof(ColorPreview)] });
        var picker = new ColorPicker
        {
            Style = Assert.IsType<Style>(Defaults.Value[typeof(ColorPicker)])
        };
        Layout(picker);
        return picker;
    }

    private static void Layout(UIElement element)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            element.Measure(new Size(240, 450));
            element.Arrange(new Rect(0, 0, 240, element.DesiredSize.Height));
            element.UpdateLayout();
        }
    }

    private static KeyEventArgs KeyArgs(UIElement target, Key key) => new(Keyboard.PrimaryDevice, new Source { RootVisual = target }, Environment.TickCount, key)
    {
        RoutedEvent = UIElement.KeyDownEvent,
        Source = target
    };
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;

        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
