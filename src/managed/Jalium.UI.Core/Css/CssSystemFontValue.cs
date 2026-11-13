namespace Jalium.UI.Styling;

// Resolve at application time: a parsed or CLI-compiled stylesheet must not
// capture the compiling machine's desktop fonts or an obsolete preference set.
internal sealed class CssSystemFontValue(string role, string component) : CssCompiledValue
{
    internal static bool IsRole(ReadOnlySpan<char> value) => value.ToString().ToLowerInvariant() is
        "caption" or "icon" or "menu" or "message-box" or "small-caption" or "status-bar";

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        context.Lengths.Fonts?.Dependency?.Observe();
        var fonts = context.Slots.SystemFonts ??= new(StringComparer.Ordinal);
        var snapshot = context.Slots.SystemFontSnapshot ??= SystemFonts.CaptureCssFonts();
        if (!fonts.TryGetValue(role, out var font)) fonts[role] = font = snapshot(role);
        CssCompiledValue value = component switch
        {
            "font-family" => new CssFontFamilyValue(new([new(font.Family.Source, false)])),
            "font-size" => new CssNamedValue("font-size", "FontSize", font.Size),
            "font-weight" => new CssNamedValue("font-weight", "FontWeight", font.Weight),
            "font-style" => new CssFontStyleValue(new(font.Style)),
            _ => throw new InvalidOperationException("Unexpected system font component."),
        };
        return value.TryApply(in context, sink);
    }
}
