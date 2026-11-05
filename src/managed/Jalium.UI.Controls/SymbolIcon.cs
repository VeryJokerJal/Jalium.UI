using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Displays a semantic symbol using a replaceable theme template. An installed
/// Segoe MDL2 font supplies the native glyph; otherwise a scalable outline is used.
/// </summary>
[TemplatePart(Name = "PART_Vector", Type = typeof(PathIcon))]
[TemplatePart(Name = "PART_Glyph", Type = typeof(FontIcon))]
public class SymbolIcon : IconElement
{
    private static readonly Lazy<bool> s_hasSymbolFont = new(IsSymbolFontAvailable);
    internal static bool HasNativeSymbolFont => s_hasSymbolFont.Value;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(nameof(Symbol), typeof(Symbol), typeof(SymbolIcon), new PropertyMetadata(Symbol.Cancel, OnSymbolChanged));
    private static readonly DependencyPropertyKey GeometryPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Geometry), typeof(Geometry), typeof(SymbolIcon), new PropertyMetadata(null));
    public static readonly DependencyProperty GeometryProperty = GeometryPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey GlyphPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Glyph), typeof(string), typeof(SymbolIcon), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GlyphProperty = GlyphPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey UsesGlyphPropertyKey = DependencyProperty.RegisterReadOnly(nameof(UsesGlyph), typeof(bool), typeof(SymbolIcon), new PropertyMetadata(false));
    public static readonly DependencyProperty UsesGlyphProperty = UsesGlyphPropertyKey.DependencyProperty;
    /// <summary>Gets or sets the semantic symbol. Existing enum aliases retain their code points.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Symbol Symbol { get => (Symbol)GetValue(SymbolProperty)!; set => SetValue(SymbolProperty, value); }
    /// <summary>Gets the font-independent outline in a 2048-unit view box, or null for an unknown symbol.</summary>
    public Geometry? Geometry => (Geometry? )GetValue(GeometryProperty);
    /// <summary>Gets the valid Unicode glyph for a font-based replacement template.</summary>
    public string Glyph => (string)GetValue(GlyphProperty)!;
    /// <summary>Gets whether the installed system icon font can provide this symbol.</summary>
    public bool UsesGlyph => (bool)GetValue(UsesGlyphProperty)!;

    public SymbolIcon()
    {
        DefaultStyleKey = typeof(SymbolIcon);
        UpdateSymbol();
    }

    public SymbolIcon(Symbol symbol) : this() => Symbol = symbol;
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateVisualStates(false);
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateVisualStates(true);
    }

    private void UpdateSymbol()
    {
        int code = (int)Symbol;
        string glyph = System.Text.Rune.IsValid(code) ? char.ConvertFromUtf32(code) : string.Empty;
        SetValue(GlyphPropertyKey, glyph);
        SetValue(GeometryPropertyKey, SymbolIconGeometry.Get(Symbol));
        SetValue(UsesGlyphPropertyKey, glyph.Length > 0 && s_hasSymbolFont.Value);
        UpdateVisualStates(false);
    }

    private void UpdateVisualStates(bool transitions)
    {
        if (TemplateRootInternal is null)
            return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", transitions);
        VisualStateManager.GoToState(this, UsesGlyph ? "Glyph" : Geometry is not null ? "Vector" : "Empty", transitions);
    }

    private static bool IsSymbolFontAvailable()
    {
        try
        {
            return NativeMethods.FontFamilyIsAvailable("Segoe MDL2 Assets") != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void OnSymbolChanged(DependencyObject source, DependencyPropertyChangedEventArgs args) => ((SymbolIcon)source).UpdateSymbol();
}
