using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Contains static properties for system-defined colors and brushes that correspond to Windows display elements.
/// </summary>
public static class SystemColors
{
    // Color properties
    public static Color ActiveBorderColor => Color.FromRgb(180, 180, 180);
    public static Color ActiveCaptionColor => Color.FromRgb(153, 180, 209);
    public static Color ActiveCaptionTextColor => Color.FromRgb(0, 0, 0);
    public static Color AppWorkspaceColor => Color.FromRgb(171, 171, 171);
    public static Color ControlColor => Color.FromRgb(240, 240, 240);
    public static Color ControlDarkColor => Color.FromRgb(160, 160, 160);
    public static Color ControlDarkDarkColor => Color.FromRgb(105, 105, 105);
    public static Color ControlLightColor => Color.FromRgb(227, 227, 227);
    public static Color ControlLightLightColor => Color.FromRgb(255, 255, 255);
    public static Color ControlTextColor => Color.FromRgb(0, 0, 0);
    public static Color DesktopColor => Color.FromRgb(0, 0, 0);
    public static Color GradientActiveCaptionColor => Color.FromRgb(185, 209, 234);
    public static Color GradientInactiveCaptionColor => Color.FromRgb(215, 228, 242);
    public static Color GrayTextColor => Color.FromRgb(109, 109, 109);
    public static Color HighlightColor => Color.FromRgb(0, 120, 215);
    public static Color HighlightTextColor => Color.FromRgb(255, 255, 255);
    public static Color HotTrackColor => Color.FromRgb(0, 102, 204);
    public static Color InactiveBorderColor => Color.FromRgb(244, 247, 252);
    public static Color InactiveCaptionColor => Color.FromRgb(191, 205, 219);
    public static Color InactiveCaptionTextColor => Color.FromRgb(0, 0, 0);
    public static Color InfoColor => Color.FromRgb(255, 255, 225);
    public static Color InfoTextColor => Color.FromRgb(0, 0, 0);
    public static Color MenuColor => Color.FromRgb(240, 240, 240);
    public static Color MenuBarColor => Color.FromRgb(240, 240, 240);
    public static Color MenuHighlightColor => Color.FromRgb(0, 120, 215);
    public static Color MenuTextColor => Color.FromRgb(0, 0, 0);
    public static Color ScrollBarColor => Color.FromRgb(200, 200, 200);
    public static Color WindowColor => Color.FromRgb(255, 255, 255);
    public static Color WindowFrameColor => Color.FromRgb(100, 100, 100);
    public static Color WindowTextColor => Color.FromRgb(0, 0, 0);

    // Brush properties (lazy-initialized)
    private static SolidColorBrush? _controlBrush;
    private static SolidColorBrush? _controlTextBrush;
    private static SolidColorBrush? _highlightBrush;
    private static SolidColorBrush? _highlightTextBrush;
    private static SolidColorBrush? _windowBrush;
    private static SolidColorBrush? _windowTextBrush;
    private static SolidColorBrush? _grayTextBrush;
    private static SolidColorBrush? _menuBrush;
    private static SolidColorBrush? _menuTextBrush;
    private static SolidColorBrush? _infoBrush;
    private static SolidColorBrush? _infoTextBrush;
    private static SolidColorBrush? _activeCaptionBrush;
    private static SolidColorBrush? _inactiveCaptionBrush;
    private static SolidColorBrush? _hotTrackBrush;
    private static SolidColorBrush? _scrollBarBrush;

    public static SolidColorBrush ControlBrush => _controlBrush ??= new SolidColorBrush(ControlColor);
    public static SolidColorBrush ControlTextBrush => _controlTextBrush ??= new SolidColorBrush(ControlTextColor);
    public static SolidColorBrush HighlightBrush => _highlightBrush ??= new SolidColorBrush(HighlightColor);
    public static SolidColorBrush HighlightTextBrush => _highlightTextBrush ??= new SolidColorBrush(HighlightTextColor);
    public static SolidColorBrush WindowBrush => _windowBrush ??= new SolidColorBrush(WindowColor);
    public static SolidColorBrush WindowTextBrush => _windowTextBrush ??= new SolidColorBrush(WindowTextColor);
    public static SolidColorBrush GrayTextBrush => _grayTextBrush ??= new SolidColorBrush(GrayTextColor);
    public static SolidColorBrush MenuBrush => _menuBrush ??= new SolidColorBrush(MenuColor);
    public static SolidColorBrush MenuTextBrush => _menuTextBrush ??= new SolidColorBrush(MenuTextColor);
    public static SolidColorBrush InfoBrush => _infoBrush ??= new SolidColorBrush(InfoColor);
    public static SolidColorBrush InfoTextBrush => _infoTextBrush ??= new SolidColorBrush(InfoTextColor);
    public static SolidColorBrush ActiveCaptionBrush => _activeCaptionBrush ??= new SolidColorBrush(ActiveCaptionColor);
    public static SolidColorBrush InactiveCaptionBrush => _inactiveCaptionBrush ??= new SolidColorBrush(InactiveCaptionColor);
    public static SolidColorBrush HotTrackBrush => _hotTrackBrush ??= new SolidColorBrush(HotTrackColor);
    public static SolidColorBrush ScrollBarBrush => _scrollBarBrush ??= new SolidColorBrush(ScrollBarColor);

    // ResourceKeys for use in XAML
    public static readonly object ControlBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(ControlBrush));
    public static readonly object ControlTextBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(ControlTextBrush));
    public static readonly object HighlightBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(HighlightBrush));
    public static readonly object HighlightTextBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(HighlightTextBrush));
    public static readonly object WindowBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(WindowBrush));
    public static readonly object WindowTextBrushKey = new ComponentResourceKey(typeof(SystemColors), nameof(WindowTextBrush));
}

/// <summary>
/// An object identifier for component resources.
/// </summary>
public sealed class ComponentResourceKey
{
    public ComponentResourceKey(Type typeInTargetAssembly, string resourceId)
    {
        TypeInTargetAssembly = typeInTargetAssembly;
        ResourceId = resourceId;
    }

    public Type TypeInTargetAssembly { get; }
    public string ResourceId { get; }

    public override bool Equals(object? obj) =>
        obj is ComponentResourceKey other &&
        TypeInTargetAssembly == other.TypeInTargetAssembly &&
        ResourceId == other.ResourceId;

    public override int GetHashCode() => HashCode.Combine(TypeInTargetAssembly, ResourceId);

    public override string ToString() => $"{TypeInTargetAssembly.Name}.{ResourceId}";
}
