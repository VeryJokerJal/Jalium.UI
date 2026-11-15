using System.Collections.ObjectModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildEffectsSection() => new GallerySampleEffectsSection();
}

public sealed record GallerySampleBackdropExample(string Title, IBackdropEffect Effect);

/// <summary>Bindable catalog of backdrop presets with a data template for each card.</summary>
public class GallerySampleEffectsSection : GallerySampleHeader
{
    public static readonly DependencyProperty ExamplesProperty = DependencyProperty.Register(nameof(Examples), typeof(System.Collections.IEnumerable), typeof(GallerySampleEffectsSection), new PropertyMetadata(null));
    public System.Collections.IEnumerable? Examples { get => (System.Collections.IEnumerable?)GetValue(ExamplesProperty); set => SetValue(ExamplesProperty, value); }
    public GallerySampleEffectsSection()
    {
        DefaultStyleKey = typeof(GallerySampleEffectsSection);
        SetCurrentValue(ExamplesProperty, new GallerySampleBackdropExample[]
        {
            new("BackdropBlurEffect (20px)", new BackdropBlurEffect(20f)),
            new("AcrylicEffect (blue tint, alpha 200)", new AcrylicEffect(Color.FromArgb(200, 30, 30, 40), tintOpacity: .6f, blurRadius: 30f)),
            new("MicaEffect", new MicaEffect()),
            new("FrostedGlassEffect (15px, noise 0.04)", new FrostedGlassEffect(blurRadius: 15f, noiseIntensity: .04f, tintColor: Color.White, tintOpacity: .3f)),
            new("ColorAdjustmentEffect - Grayscale", ColorAdjustmentEffect.CreateGrayscale(1f)),
            new("ColorAdjustmentEffect - Sepia", ColorAdjustmentEffect.CreateSepia(.8f)),
            new("ColorAdjustmentEffect - Invert", ColorAdjustmentEffect.CreateInvert(1f)),
            new("ColorAdjustmentEffect - Hue Rotate 90°", ColorAdjustmentEffect.CreateHueRotate(90f)),
            new("Box kernel + Opacity 0.5", new BackdropBlurEffect(24f, BackdropBlurType.Box) { Opacity = .5f }),
        });
    }
}

/// <summary>A material panel over twenty-four colored tiles. Its layout is fully replaceable.</summary>
[TemplatePart(Name = "PART_EffectPanel", Type = typeof(Border))]
public class GallerySampleBackdropDemo : GallerySampleDemo
{
    public static readonly DependencyProperty EffectNameProperty = DependencyProperty.Register(nameof(EffectName), typeof(string), typeof(GallerySampleBackdropDemo), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty MaterialProperty = DependencyProperty.Register(nameof(Material), typeof(IBackdropEffect), typeof(GallerySampleBackdropDemo), new PropertyMetadata(null, (d, e) => ((GallerySampleBackdropDemo)d).SetCurrentValue(EffectNameProperty, e.NewValue?.GetType().Name ?? string.Empty)));
    public static readonly DependencyProperty TilesProperty = DependencyProperty.Register(nameof(Tiles), typeof(System.Collections.IEnumerable), typeof(GallerySampleBackdropDemo), new PropertyMetadata(null));
    public IBackdropEffect? Material { get => (IBackdropEffect?)GetValue(MaterialProperty); set => SetValue(MaterialProperty, value); }
    public string EffectName { get => (string)GetValue(EffectNameProperty)!; set => SetValue(EffectNameProperty, value); }
    public System.Collections.IEnumerable? Tiles { get => (System.Collections.IEnumerable?)GetValue(TilesProperty); set => SetValue(TilesProperty, value); }
    public GallerySampleBackdropDemo()
    {
        DefaultStyleKey = typeof(GallerySampleBackdropDemo);
        SetCurrentValue(TilesProperty, TileColors.Select(color => new SolidColorBrush(color)).ToArray());
    }
    private static readonly Color[] TileColors =
    [
        Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0xFF, 0x7F, 0x00), Color.FromRgb(0xFF, 0xFF, 0x00),
        Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0x00, 0x00, 0xFF), Color.FromRgb(0x00, 0x82, 0x2B),
        Color.FromRgb(0x00, 0xD3, 0x46), Color.FromRgb(0x14, 0xFF, 0x62),
        Color.FromRgb(0x00, 0xCE, 0xD1), Color.FromRgb(0xFF, 0x63, 0x47), Color.FromRgb(0x32, 0xCD, 0x32),
        Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(0x14, 0xDC, 0x57), Color.FromRgb(0x00, 0xBF, 0xFF),
        Color.FromRgb(0x69, 0xFF, 0x9B), Color.FromRgb(0x7F, 0xFF, 0x00),
        Color.FromRgb(0x3F, 0x51, 0xB5), Color.FromRgb(0x21, 0x96, 0xF3), Color.FromRgb(0x03, 0xA9, 0xF4),
        Color.FromRgb(0x00, 0xBC, 0xD4), Color.FromRgb(0x00, 0x96, 0x88), Color.FromRgb(0xF4, 0x43, 0x36),
        Color.FromRgb(0x8B, 0xC3, 0x4A), Color.FromRgb(0x79, 0x55, 0x48),
    ];

}
